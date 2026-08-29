using System.Security.Claims;
using System.Text.Encodings.Web;
using Atua.Api.Application.Identity;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atua.Api.Infrastructure.Security;

public sealed class ServiceCredentialAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITokenHashService tokenHashService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ServiceCredential";
    public const string EligibilityScope = "collector.eligibility.read";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization[prefix.Length..]))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization.Substring(prefix.Length);
        var tokenHash = tokenHashService.Hash(token);
        var dbContext = Context.RequestServices.GetRequiredService<AtuaDbContext>();
        var credential = await dbContext.ServiceCredentials.AsNoTracking().SingleOrDefaultAsync(
            item => item.TokenHash == tokenHash &&
                    item.RevokedAt == null &&
                    item.Scope == EligibilityScope,
            Context.RequestAborted);
        if (credential is null) return AuthenticateResult.Fail("Invalid service credential.");

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim("tenant_id", credential.TenantId.ToString()));
        identity.AddClaim(new Claim("integration_id", credential.IntegrationId.ToString()));
        identity.AddClaim(new Claim("provider_id", credential.ProviderId.ToString()));
        identity.AddClaim(new Claim("scope", credential.Scope));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
