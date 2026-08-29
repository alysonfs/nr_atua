using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Globalization;
using Atua.Api.Domain.Identity;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Atua.Api.Application.Identity;

public sealed class AuthService(
    AtuaDbContext dbContext,
    ISecretHasher secretHasher,
    ITokenHashService tokenHashService,
    IRefreshTokenStore refreshTokenStore,
    TimeProvider timeProvider,
    IOptions<AuthOptions> options)
{
    private readonly AuthOptions authOptions = options.Value;

    public async Task<AuthTokens?> SignInAsync(string email, string password,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Email == email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null || user.EmailConfirmedAt is null ||
            !secretHasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var session = new AuthSession(Guid.CreateVersion7(), user.Id, now);
        dbContext.AuthSessions.Add(session);
        var refreshToken = AddRefreshToken(session.Id, now, Guid.CreateVersion7());
        await dbContext.SaveChangesAsync(cancellationToken);
        return new AuthTokens(CreateAccessToken(user, session.Id, now), refreshToken);
    }

    public async Task<AuthTokens?> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var token = await dbContext.AuthRefreshTokens.SingleOrDefaultAsync(
            candidate => candidate.TokenHash == tokenHashService.Hash(rawRefreshToken), cancellationToken);
        if (token is null)
        {
            return null;
        }

        var session = await dbContext.AuthSessions.SingleAsync(
            candidate => candidate.Id == token.SessionId, cancellationToken);
        if (session.RevokedAt is not null || token.ExpiresAt <= now)
        {
            return null;
        }

        if (dbContext.Database.IsRelational())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            return await ConsumeAndRotateAsync(token, session, now, cancellationToken, transaction);
        }

        return await ConsumeAndRotateAsync(token, session, now, cancellationToken, null);
    }

    private async Task<AuthTokens?> ConsumeAndRotateAsync(AuthRefreshToken token, AuthSession session,
        DateTimeOffset now, CancellationToken cancellationToken,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction)
    {
        if (!await refreshTokenStore.TryConsumeAsync(token.Id, now, cancellationToken))
        {
            session.Revoke(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var user = await dbContext.Users.SingleAsync(user => user.Id == session.UserId, cancellationToken);
        var nextRefreshToken = AddRefreshToken(session.Id, now, token.FamilyId);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new AuthTokens(CreateAccessToken(user, session.Id, now), nextRefreshToken);
    }

    public async Task SignOutAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.AuthSessions.FindAsync(new object?[] { sessionId }, cancellationToken);
        if (session is not null)
        {
            session.Revoke(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private string AddRefreshToken(Guid sessionId, DateTimeOffset now, Guid familyId)
    {
        var rawToken = tokenHashService.GenerateToken();
        dbContext.AuthRefreshTokens.Add(new AuthRefreshToken(Guid.CreateVersion7(), sessionId,
            familyId, tokenHashService.Hash(rawToken), now.AddDays(authOptions.RefreshTokenLifetimeDays)));
        return rawToken;
    }

    private string CreateAccessToken(User user, Guid sessionId, DateTimeOffset now)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Sid, sessionId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            new Claim("role", user.GlobalRole.ToString())
        };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            authOptions.Issuer, authOptions.Audience, claims, now.UtcDateTime,
            now.AddMinutes(15).UtcDateTime, credentials));
    }
}

public sealed record AuthTokens(string AccessToken, string RefreshToken);
