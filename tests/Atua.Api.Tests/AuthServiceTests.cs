using System.IdentityModel.Tokens.Jwt;
using Atua.Api.Application.Identity;
using Atua.Api.Domain.Identity;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task EmiteJwtComClaimsPermitidasESomenteHashDoRefreshPersistido()
    {
        await using var context = CreateContext();
        var user = ConfirmedUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var tokens = await service.SignInAsync(user.Email, "senha123", CancellationToken.None);

        Assert.NotNull(tokens);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        Assert.Contains(jwt.Claims, claim => claim.Type == "sub" && claim.Value == user.Id.ToString());
        Assert.Contains(jwt.Claims, claim => claim.Type == "sid");
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == "tenant_id");
        var persisted = await context.AuthRefreshTokens.SingleAsync();
        Assert.NotEqual(tokens.RefreshToken, persisted.TokenHash);
    }

    [Fact]
    public async Task ReusoDoRefreshRevogaSessao()
    {
        await using var context = CreateContext();
        var user = ConfirmedUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = CreateService(context);
        var initial = (await service.SignInAsync(user.Email, "senha123", CancellationToken.None))!;

        var rotated = await service.RefreshAsync(initial.RefreshToken, CancellationToken.None);
        var replay = await service.RefreshAsync(initial.RefreshToken, CancellationToken.None);

        Assert.NotNull(rotated);
        Assert.Null(replay);
        Assert.NotNull((await context.AuthSessions.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task SignoutRevogaSid()
    {
        await using var context = CreateContext();
        var user = ConfirmedUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = CreateService(context);
        _ = await service.SignInAsync(user.Email, "senha123", CancellationToken.None);
        var sessionId = (await context.AuthSessions.SingleAsync()).Id;

        await service.SignOutAsync(sessionId, CancellationToken.None);

        Assert.NotNull((await context.AuthSessions.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task NaoEmiteSucessorQuandoCompareAndSetDoConsumoFalha()
    {
        await using var context = CreateContext();
        var user = ConfirmedUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var initial = (await CreateService(context).SignInAsync(user.Email, "senha123",
            CancellationToken.None))!;

        var result = await CreateService(context, new FailedConsumptionStore()).RefreshAsync(
            initial.RefreshToken, CancellationToken.None);

        Assert.Null(result);
        Assert.Single(await context.AuthRefreshTokens.ToListAsync());
        Assert.NotNull((await context.AuthSessions.SingleAsync()).RevokedAt);
    }

    private static AuthService CreateService(AtuaDbContext context,
        IRefreshTokenStore? refreshTokenStore = null) => new(context,
        new FakeSecretHasher(), new TokenHashService(), refreshTokenStore ?? new RefreshTokenStore(context),
        new FixedTimeProvider(),
        Options.Create(new AuthOptions { SigningKey = "01234567890123456789012345678901" }));

    private static User ConfirmedUser()
    {
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash:senha123");
        user.ConfirmEmail(DateTimeOffset.UtcNow);
        return user;
    }

    private static AtuaDbContext CreateContext() => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeSecretHasher : ISecretHasher
    {
        public string Hash(string value) => $"hash:{value}";
        public bool Verify(string value, string hash) => hash == Hash(value);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FailedConsumptionStore : IRefreshTokenStore
    {
        public Task<bool> TryConsumeAsync(Guid tokenId, DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
