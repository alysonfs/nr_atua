using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atua.Api.Application.Billing;
using Atua.Api.Application.Identity;
using Atua.Api.Application.Integrations;
using Atua.Api.Application.Tenants;
using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

public class TenantEndpointsTests
{
    [Fact]
    public async Task GetMyTenantsRetornaListaVaziaEDefaultNuloQuandoSemMembership()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var client = CreateAuthenticatedClient(app, Guid.CreateVersion7());

        var response = await client.GetAsync("/api/users/me/tenants");
        var body = await response.Content.ReadFromJsonAsync<GetMyTenantsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(body!.Tenants);
        Assert.Null(body.DefaultTenantId);
    }

    [Fact]
    public async Task GetMyTenantsRetornaDefaultTenantIdQuandoUnicoMembership()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var userId = Guid.CreateVersion7();
        await SeedTenantMembershipAsync(databaseName, userId, ETenantMembershipRole.Owner);
        var client = CreateAuthenticatedClient(app, userId);

        var response = await client.GetAsync("/api/users/me/tenants");
        var body = await response.Content.ReadFromJsonAsync<GetMyTenantsResponse>();

        Assert.Single(body!.Tenants);
        Assert.NotNull(body.DefaultTenantId);
        Assert.Equal(body.Tenants.Single().TenantId, body.DefaultTenantId);
    }

    [Fact]
    public async Task PostTenantsCriaTenantEMembershipOwnerQuandoCnpjValidoEUnico()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var userId = Guid.CreateVersion7();
        await SeedUserWithTrialAsync(databaseName, userId);
        var client = CreateAuthenticatedClient(app, userId);

        var response = await client.PostAsJsonAsync("/api/tenants",
            new CreateTenantRequest("Atua Refrigeração", "11122233000183"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        Assert.NotEqual(Guid.Empty, body!.TenantId);
    }

    [Fact]
    public async Task PostTenantsRetornaIntegrationIdValidoNaoNulo()
    {
        // Emenda ADR-018 ("Resolução de integrationId"): POST /api/tenants
        // deve retornar o integrationId da Integration iService criada
        // automaticamente, permitindo ao frontend montar a rota de
        // credenciais sem um endpoint de descoberta.
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var userId = Guid.CreateVersion7();
        await SeedUserWithTrialAsync(databaseName, userId);
        var client = CreateAuthenticatedClient(app, userId);

        var response = await client.PostAsJsonAsync("/api/tenants",
            new CreateTenantRequest("Atua Refrigeração", "11122233000183"));
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.IntegrationId);

        await using var context = CreateContext(databaseName);
        var integration = await context.Integrations.SingleAsync(item => item.Id == body.IntegrationId);
        Assert.Equal(body.TenantId, integration.TenantId);
        Assert.False(integration.IsEnabled);
    }

    [Fact]
    public async Task GetMyTenantsRetornaIntegrationIdCorretamente()
    {
        // Emenda ADR-018 ("Resolução de integrationId"): GET
        // /api/users/me/tenants deve incluir o integrationId de cada
        // tenant, cobrindo o caso de uma sessão nova sem repetir onboarding.
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var (tenantId, integrationId, ownerId) = await SeedTenantWithIntegrationAsync(databaseName);
        var client = CreateAuthenticatedClient(app, ownerId);

        var response = await client.GetAsync("/api/users/me/tenants");
        var body = await response.Content.ReadFromJsonAsync<GetMyTenantsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tenant = Assert.Single(body!.Tenants);
        Assert.Equal(tenantId, tenant.TenantId);
        Assert.Equal(integrationId, tenant.IntegrationId);
    }

    [Fact]
    public async Task PostTenantsRetorna409QuandoCnpjJaRegistrado()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        await using (var seedContext = CreateContext(databaseName))
        {
            seedContext.Tenants.Add(new Tenant(Guid.CreateVersion7(), "Outra", "11122233000183",
                "America/Sao_Paulo"));
            await seedContext.SaveChangesAsync();
        }
        var userId = Guid.CreateVersion7();
        await SeedUserWithTrialAsync(databaseName, userId);
        var client = CreateAuthenticatedClient(app, userId);

        var response = await client.PostAsJsonAsync("/api/tenants",
            new CreateTenantRequest("Minha Empresa", "11122233000183"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutCredentialsRetorna403QuandoUsuarioNaoEOwner()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var (tenantId, integrationId, ownerId) = await SeedTenantWithIntegrationAsync(databaseName);
        var adminId = Guid.CreateVersion7();
        await using (var context = CreateContext(databaseName))
        {
            context.Users.Add(new User(adminId, null, "admin@atua.com", "hash"));
            context.TenantMemberships.Add(new TenantMembership(tenantId, adminId, ETenantMembershipRole.Admin));
            await context.SaveChangesAsync();
        }
        var client = CreateAuthenticatedClient(app, adminId);

        var response = await client.PutAsJsonAsync(
            $"/api/tenants/{tenantId}/integrations/{integrationId}/credentials",
            new SetCredentialsRequest("usuario", "senha", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCredentialsNuncaExpoeSegredoNoCorpoDaResposta()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var (tenantId, integrationId, ownerId) = await SeedTenantWithIntegrationAsync(databaseName);
        var client = CreateAuthenticatedClient(app, ownerId);
        await client.PutAsJsonAsync($"/api/tenants/{tenantId}/integrations/{integrationId}/credentials",
            new SetCredentialsRequest("usuario.secreto", "senha-super-secreta", null));

        var response = await client.GetAsync(
            $"/api/tenants/{tenantId}/integrations/{integrationId}/credentials");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("usuario.secreto", raw);
        Assert.DoesNotContain("senha-super-secreta", raw);
        var body = await response.Content.ReadFromJsonAsync<GetCredentialsResponse>();
        Assert.True(body!.HasCredentials);
        Assert.Equal("NotValidated", body.ValidationStatus);
    }

    [Fact]
    public async Task PutCredentialsResetaValidationStatusParaNotValidated()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var (tenantId, integrationId, ownerId) = await SeedTenantWithIntegrationAsync(databaseName);
        var client = CreateAuthenticatedClient(app, ownerId);
        await client.PutAsJsonAsync($"/api/tenants/{tenantId}/integrations/{integrationId}/credentials",
            new SetCredentialsRequest("usuario", "senha-valida", null));
        await client.PostAsync(
            $"/api/tenants/{tenantId}/integrations/{integrationId}/credentials/validate", null);

        await client.PutAsJsonAsync($"/api/tenants/{tenantId}/integrations/{integrationId}/credentials",
            new SetCredentialsRequest("usuario2", "senha2", null));

        var response = await client.GetAsync(
            $"/api/tenants/{tenantId}/integrations/{integrationId}/credentials");
        var body = await response.Content.ReadFromJsonAsync<GetCredentialsResponse>();

        Assert.Equal("NotValidated", body!.ValidationStatus);
        Assert.Null(body.LastValidatedAtUtc);
    }

    [Fact]
    public async Task ValidateRetorna404QuandoCredenciaisNaoConfiguradas()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var (tenantId, integrationId, ownerId) = await SeedTenantWithIntegrationAsync(databaseName);
        var client = CreateAuthenticatedClient(app, ownerId);

        var response = await client.PostAsync(
            $"/api/tenants/{tenantId}/integrations/{integrationId}/credentials/validate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ValidateRetornaSucceededQuandoClientFakeAutentica()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var (tenantId, integrationId, ownerId) = await SeedTenantWithIntegrationAsync(databaseName);
        var client = CreateAuthenticatedClient(app, ownerId);
        await client.PutAsJsonAsync($"/api/tenants/{tenantId}/integrations/{integrationId}/credentials",
            new SetCredentialsRequest("usuario", "senha-valida", null));

        var response = await client.PostAsync(
            $"/api/tenants/{tenantId}/integrations/{integrationId}/credentials/validate", null);
        var body = await response.Content.ReadFromJsonAsync<ValidateCredentialsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Succeeded", body!.ValidationStatus);
    }

    private static async Task<(Guid TenantId, Guid IntegrationId, Guid OwnerId)>
        SeedTenantWithIntegrationAsync(string databaseName)
    {
        var tenantId = Guid.CreateVersion7();
        var integrationId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        await using var context = CreateContext(databaseName);
        var tenant = new Tenant(tenantId, "Atua", "11122233000183", "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), "iService", "Fabricante",
            new Uri("https://provider.example.com"), true);
        var integration = new Atua.Api.Domain.Integrations.Integration(integrationId, tenantId, provider.Id, true);
        var owner = new User(ownerId, null, "owner@atua.com", "hash");
        context.AddRange(tenant, provider, integration, owner);
        context.TenantMemberships.Add(new TenantMembership(tenantId, ownerId, ETenantMembershipRole.Owner));
        await context.SaveChangesAsync();
        return (tenantId, integrationId, ownerId);
    }

    private static async Task SeedTenantMembershipAsync(string databaseName, Guid userId,
        ETenantMembershipRole role)
    {
        await using var context = CreateContext(databaseName);
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "11122233000183", "America/Sao_Paulo");
        var user = new User(userId, null, "user@atua.com", "hash");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), "iService", "Fabricante",
            new Uri("https://provider.example.com"), true);
        var integration = new Atua.Api.Domain.Integrations.Integration(Guid.CreateVersion7(), tenant.Id,
            provider.Id, false);
        context.AddRange(tenant, user, provider, integration);
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, userId, role));
        await context.SaveChangesAsync();
    }

    private static async Task SeedUserWithTrialAsync(string databaseName, Guid userId)
    {
        await using var context = CreateContext(databaseName);
        var user = new User(userId, null, "user@atua.com", "hash");
        user.ConfirmEmail(DateTimeOffset.UtcNow);
        context.Users.Add(user);
        if (!await context.Plans.AnyAsync(plan => plan.Id == WellKnownPlans.TrialPlanId))
        {
            context.Plans.Add(new Plan(WellKnownPlans.TrialPlanId, WellKnownPlans.TrialCode, "Trial",
                isFree: true, value: 0m, durationDays: 7, maxIntegrations: 2, maxUsers: 5, isActive: true));
        }
        await context.SaveChangesAsync();
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
        builder.Services.Configure<CredentialCipherOptions>(options =>
        {
            options.MasterKeyBase64 = Convert.ToBase64String(new byte[32]);
        });
        builder.Services.AddSingleton<ICredentialCipher, AesGcmCredentialCipher>();
        builder.Services.AddSingleton<IIServiceAuthClient, FakeIServiceAuthClient>();
        builder.Services.AddScoped<AddTenantUseCase>();
        builder.Services.AddScoped<GetTenantPlanUseCase>();
        builder.Services.AddScoped<ChangeTenantPlanUseCase>();
        builder.Services.AddScoped<ChangeTenantMembershipRoleUseCase>();
        builder.Services.AddScoped<Atua.Api.Application.Integrations.CollectorControl.ICollectorEligibilityEvaluator,
            Atua.Api.Application.Integrations.CollectorControl.CollectorEligibilityEvaluator>();
        builder.Services.AddScoped<Atua.Api.Application.Integrations.CollectorControl.CollectorActivationService>();
        builder.Services.AddScoped<IServiceCredentialService>();
        builder.Services.AddScoped<IServiceCredentialValidationService>();
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
        app.MapTenantEndpoints();
        await app.StartAsync();
        return (app, capturedDatabaseName);
    }

    private static HttpClient CreateAuthenticatedClient(WebApplication app, Guid userId)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Test-User-Id", userId.ToString());
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
            var ticket = new AuthenticationTicket(principal, "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
