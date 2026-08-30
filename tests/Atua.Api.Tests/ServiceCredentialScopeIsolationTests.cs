using System.Net;
using Atua.Api.Application.Billing;
using Atua.Api.Application.Identity;
using Atua.Api.Application.Integrations.CollectorControl;
using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Integrations.CollectorControl;
using Atua.Api.Domain.Tenants;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

/// <summary>
/// QA — Ponto de atenção 1 (regressão de segurança / RF-009 / ADR-021).
///
/// Verifica que o isolamento de scope é aplicado corretamente depois que
/// <see cref="ServiceCredentialAuthenticationHandler"/> passou a aceitar
/// qualquer credential ativa no banco (sem filtrar por scope fixo) e delegou
/// o controle de scope para as policies de authorization.
///
/// Cenários:
/// 1. Credential com scope collector.command.claim → GET eligibility → 403.
/// 2. Credential com scope collector.command.complete → GET eligibility → 403.
/// 3. Credential com scope collector.eligibility.read → GET eligibility → não 401/403.
/// 4. Credential com scope collector.eligibility.read → POST claim → 403.
/// 5. Credential com scope collector.command.claim → POST claim → 204 (sem comando pendente).
/// 6. Credential com scope collector.command.claim → POST complete → 403.
/// </summary>
public class ServiceCredentialScopeIsolationTests
{
    [Fact]
    public async Task ClaimScope_NaoAcessaEligibilityEndpoint_Retorna403()
    {
        var (app, dbName) = await CreateApplicationAsync();
        await using var _ = app;
        var (tenantId, integrationId, providerId) = await SeedBaseAsync(dbName);

        var token = await SeedServiceCredentialAsync(dbName, tenantId, integrationId, providerId,
            ServiceCredentialAuthenticationHandler.ClaimScope);

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.GetAsync("/api/internal/collector/eligibility");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CompleteScope_NaoAcessaEligibilityEndpoint_Retorna403()
    {
        var (app, dbName) = await CreateApplicationAsync();
        await using var _ = app;
        var (tenantId, integrationId, providerId) = await SeedBaseAsync(dbName);

        var token = await SeedServiceCredentialAsync(dbName, tenantId, integrationId, providerId,
            ServiceCredentialAuthenticationHandler.CompleteScope);

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.GetAsync("/api/internal/collector/eligibility");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EligibilityScope_AcessaEligibilityEndpoint_NaoRetorna401Nem403()
    {
        var (app, dbName) = await CreateApplicationAsync();
        await using var _ = app;
        var (tenantId, integrationId, providerId) = await SeedBaseAsync(dbName);
        await SeedActivationAsync(dbName, tenantId, integrationId);

        var token = await SeedServiceCredentialAsync(dbName, tenantId, integrationId, providerId,
            ServiceCredentialAuthenticationHandler.EligibilityScope);

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.GetAsync("/api/internal/collector/eligibility");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EligibilityScope_NaoAcessaClaimEndpoint_Retorna403()
    {
        var (app, dbName) = await CreateApplicationAsync();
        await using var _ = app;
        var (tenantId, integrationId, providerId) = await SeedBaseAsync(dbName);

        var token = await SeedServiceCredentialAsync(dbName, tenantId, integrationId, providerId,
            ServiceCredentialAuthenticationHandler.EligibilityScope);

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.PostAsync("/api/internal/collector/commands/claim", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ClaimScope_AcessaClaimEndpoint_Retorna204QuandoSemComandoPendente()
    {
        var (app, dbName) = await CreateApplicationAsync();
        await using var _ = app;
        var (tenantId, integrationId, providerId) = await SeedBaseAsync(dbName);

        var token = await SeedServiceCredentialAsync(dbName, tenantId, integrationId, providerId,
            ServiceCredentialAuthenticationHandler.ClaimScope);

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.PostAsync("/api/internal/collector/commands/claim", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ClaimScope_NaoAcessaCompleteEndpoint_Retorna403()
    {
        var (app, dbName) = await CreateApplicationAsync();
        await using var _ = app;
        var (tenantId, integrationId, providerId) = await SeedBaseAsync(dbName);

        var token = await SeedServiceCredentialAsync(dbName, tenantId, integrationId, providerId,
            ServiceCredentialAuthenticationHandler.ClaimScope);

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var fakeCommandId = Guid.CreateVersion7();
        var response = await client.PostAsync(
            $"/api/internal/collector/commands/{fakeCommandId}/complete", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<(WebApplication App, string DbName)> CreateApplicationAsync()
    {
        var dbName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AtuaDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ITokenHashService, TokenHashService>();
        builder.Services.AddScoped<TrialEligibilityService>();
        builder.Services.AddScoped<TimeZonePreferenceService>();
        builder.Services.AddScoped<ICollectorEligibilityEvaluator, CollectorEligibilityEvaluator>();
        builder.Services.AddScoped<CollectorActivationService>();
        builder.Services.AddScoped<ImmediateCollectionCommandService>();
        builder.Services.AddSingleton<IOptions<ImmediateCollectionOptions>>(
            _ => Options.Create(new ImmediateCollectionOptions()));
        builder.Services.AddScoped<ICredentialCipher, FakeCipher>();

        builder.Services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ServiceCredentialAuthenticationHandler>(
                ServiceCredentialAuthenticationHandler.SchemeName, _ => { });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("CollectorEligibility", policy =>
                policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
                    .RequireClaim("scope", ServiceCredentialAuthenticationHandler.EligibilityScope));
            options.AddPolicy("CollectorCommandClaim", policy =>
                policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
                    .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ClaimScope));
            options.AddPolicy("CollectorCommandComplete", policy =>
                policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
                    .RequireClaim("scope", ServiceCredentialAuthenticationHandler.CompleteScope));
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapTrialEndpoints();
        app.MapCollectorCommandEndpoints();
        await app.StartAsync();
        return (app, dbName);
    }

    private static async Task<(Guid TenantId, Guid IntegrationId, Guid ProviderId)>
        SeedBaseAsync(string dbName)
    {
        await using var ctx = CreateContext(dbName);
        var tenantId = Guid.CreateVersion7();
        var integrationId = Guid.CreateVersion7();
        var providerId = Guid.CreateVersion7();

        ctx.Tenants.Add(new Tenant(tenantId, "Empresa Test", "12.345.678/0001-99", "America/Sao_Paulo"));
        ctx.IntegrationProviders.Add(new IntegrationProvider(providerId, "iService Test",
            "iservice-test", new Uri("https://test.example.com"), isActive: true));
        ctx.Integrations.Add(new Integration(integrationId, tenantId, providerId, isEnabled: true));
        await ctx.SaveChangesAsync();

        return (tenantId, integrationId, providerId);
    }

    private static async Task SeedActivationAsync(string dbName, Guid tenantId, Guid integrationId)
    {
        await using var ctx = CreateContext(dbName);
        ctx.CollectorActivations.Add(new CollectorActivation(Guid.CreateVersion7(), tenantId, integrationId));
        await ctx.SaveChangesAsync();
    }

    private static async Task<string> SeedServiceCredentialAsync(
        string dbName, Guid tenantId, Guid integrationId, Guid providerId, string scope)
    {
        var rawToken = Guid.NewGuid().ToString("N");
        var hashService = new TokenHashService();
        var tokenHash = hashService.Hash(rawToken);

        await using var ctx = CreateContext(dbName);
        ctx.ServiceCredentials.Add(new ServiceCredential(
            Guid.CreateVersion7(), tenantId, integrationId, providerId, tokenHash, scope));
        await ctx.SaveChangesAsync();

        return rawToken;
    }

    private static AtuaDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AtuaDbContext>().UseInMemoryDatabase(dbName).Options);

    private sealed class FakeCipher : ICredentialCipher
    {
        public EncryptedDataKey CreateDataKey() =>
            new(new byte[32], "fake-dek", Guid.Empty);

        public CipherResult Encrypt(byte[] dataKeyPlaintext, string plaintext) =>
            new("fake-ciphertext", new byte[12], new byte[16]);

        public string Decrypt(byte[] dataKeyPlaintext, string ciphertextBase64,
            byte[] nonce, byte[] tag) => "fake-decrypted";

        public byte[] UnwrapDataKey(string dataKeyCiphertextBase64, Guid kmsKeyId) =>
            new byte[32];
    }
}
