using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atua.Api.Application.Integrations.CollectorControl;
using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Integrations.CollectorControl;
using Atua.Api.Domain.Tenants;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atua.Api.Tests;

/// <summary>
/// Contrato Office de RF-008/ADR-020:
/// GET/PUT/DELETE .../collector-activation.
/// </summary>
public class CollectorActivationEndpointsTests
{
    [Fact]
    public async Task PutAtivaERetornaEstadoComComandoPendente()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var client = CreateClient(app, seed.OwnerId, "chave-1");

        var response = await client.PutAsync(RouteFor(seed), null);
        var body = await response.Content.ReadFromJsonAsync<CollectorActivationView>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Active", body!.Status);
        Assert.Equal("Pending", body.LastImmediateCommand!.Status);
    }

    [Fact]
    public async Task PutRetorna409ActivationNotEligibleQuandoTrialInelegivel()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName, trialActive: false,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var client = CreateClient(app, seed.OwnerId, "chave-1");

        var response = await client.PutAsync(RouteFor(seed), null);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("activation_not_eligible", raw);

        await using var context = CreateContext(databaseName);
        Assert.Empty(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task PutAtivaMesmoSemCredencialValidada()
    {
        // ADR-024: a ativação não é mais bloqueada por validação de
        // credencial iService, apenas pela elegibilidade de plano.
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName, trialActive: true, credentialStatus: null);
        var client = CreateClient(app, seed.OwnerId, "chave-1");

        var response = await client.PutAsync(RouteFor(seed), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutSemIdempotencyKeyRetorna400()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var client = CreateClient(app, seed.OwnerId, idempotencyKey: null);

        var response = await client.PutAsync(RouteFor(seed), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutRepetidoComMesmaChaveNaoCriaSegundoComando()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var client = CreateClient(app, seed.OwnerId, "chave-1");

        await client.PutAsync(RouteFor(seed), null);
        var second = await client.PutAsync(RouteFor(seed), null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await using var context = CreateContext(databaseName);
        Assert.Single(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task DeleteDesativaECancelaComandoPendente()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var client = CreateClient(app, seed.OwnerId, "chave-1");
        await client.PutAsync(RouteFor(seed), null);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "chave-2");
        var response = await client.DeleteAsync(RouteFor(seed));
        var body = await response.Content.ReadFromJsonAsync<CollectorActivationView>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Inactive", body!.Status);
        Assert.Equal("Cancelled", body.LastImmediateCommand!.Status);
    }

    [Fact]
    public async Task GetRetorna403ParaUsuarioSemMembership()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var client = CreateClient(app, Guid.CreateVersion7(), "chave-1");

        var response = await client.GetAsync(RouteFor(seed));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutRetorna403ParaUsuarioDeOutroTenant()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var victim = await SeedAsync(databaseName, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var attacker = await SeedAsync(databaseName, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded, cnpj: "22233344000199",
            emailPrefix: "outro");
        var client = CreateClient(app, attacker.OwnerId, "chave-1");

        var response = await client.PutAsync(RouteFor(victim), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var context = CreateContext(databaseName);
        Assert.Empty(context.ImmediateCollectionCommands);
    }

    [Fact]
    public async Task GetNuncaExpoeSegredoNoCorpoDaResposta()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName, trialActive: true,
            credentialStatus: EIServiceValidationStatus.Succeeded);
        var client = CreateClient(app, seed.OwnerId, "chave-1");
        await client.PutAsync(RouteFor(seed), null);

        var response = await client.GetAsync(RouteFor(seed));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("cipher", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ciphertext", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expiresAt", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", raw, StringComparison.OrdinalIgnoreCase);
    }

    private static string RouteFor(Seed seed) =>
        $"/api/tenants/{seed.TenantId}/integrations/{seed.IntegrationId}/collector-activation";

    private sealed record Seed(Guid TenantId, Guid IntegrationId, Guid OwnerId);

    private static async Task<Seed> SeedAsync(string databaseName, bool trialActive,
        EIServiceValidationStatus? credentialStatus, string cnpj = "11122233000183",
        string emailPrefix = "atua")
    {
        await using var context = CreateContext(databaseName);
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", cnpj, "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), $"iService-{emailPrefix}",
            "Fabricante", new Uri("https://provider.example.com"), true);
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var owner = new User(Guid.CreateVersion7(), null, $"{emailPrefix}.owner@atua.com", "hash");
        context.AddRange(tenant, provider, integration, owner);
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, owner.Id, ETenantMembershipRole.Owner));

        if (trialActive)
        {
            var plan = new Plan(Guid.CreateVersion7(), "trial", "Trial", isFree: true, value: 0m,
                durationDays: 7, maxIntegrations: 2, maxUsers: 5, isActive: true);
            context.Plans.Add(plan);
            context.TenantPlans.Add(new TenantPlan(Guid.CreateVersion7(), tenant.Id, plan.Id,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)));
        }

        if (credentialStatus is not null)
        {
            var credential = new IServiceCredential(Guid.CreateVersion7(), tenant.Id, integration.Id,
                "cipher-user", "cipher-pass", null, [1], [2], "cipher-key", "local-v1", 1,
                DateTimeOffset.UtcNow);
            credential.RecordValidation(credentialStatus.Value, DateTimeOffset.UtcNow);
            context.IServiceCredentials.Add(credential);
        }

        await context.SaveChangesAsync();
        return new Seed(tenant.Id, integration.Id, owner.Id);
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(
        new DbContextOptionsBuilder<AtuaDbContext>().UseInMemoryDatabase(databaseName).Options);

    private static async Task<(WebApplication App, string DatabaseName)> CreateApplicationAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var capturedDatabaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AtuaDbContext>(options =>
            options.UseInMemoryDatabase(capturedDatabaseName));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ICollectorEligibilityEvaluator, CollectorEligibilityEvaluator>();
        builder.Services.AddScoped<CollectorActivationService>();
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("BrowserSession", policy =>
                policy.AddAuthenticationSchemes("Test").RequireAuthenticatedUser());
        });
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapCollectorActivationEndpoints();
        await app.StartAsync();
        return (app, capturedDatabaseName);
    }

    private static HttpClient CreateClient(WebApplication app, Guid userId, string? idempotencyKey)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Test-User-Id", userId.ToString());
        if (idempotencyKey is not null)
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        }

        return client;
    }

    private sealed class TestAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Test-User-Id", out var userId))
            {
                return Task.FromResult(AuthenticateResult.Fail("missing user"));
            }

            var claims = new[] { new Claim("sub", userId.ToString()!) };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, "Test")));
        }
    }
}
