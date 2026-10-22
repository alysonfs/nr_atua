using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atua.Api.Tests;

/// <summary>
/// Testes HTTP do pareamento via QR (ADR-049):
/// <c>POST /api/internal/channel/session</c> (credencial de serviço,
/// escopo exclusivo <c>channel.session.report</c>) e
/// <c>GET /api/tenants/{tenantId}/whatsapp-session</c> (sessão de
/// navegador, restrito a OWNER/ADMIN). Usa <see cref="TestServer"/> com EF
/// InMemory e um esquema de autenticação de teste único que simula ambos
/// os conjuntos de claims (credencial de serviço e sessão de navegador),
/// seguindo o mesmo padrão de <c>TriagemChannelEndpointsTests</c>.
/// </summary>
public class WhatsAppSessionEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    // -------------------------------------------------------------------
    // Report (Channel -> API)
    // -------------------------------------------------------------------

    [Fact]
    public async Task Report_QrPending_Retorna204EDevolvidoPeloGetAteExpirar()
    {
        var (app, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var tenantId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var serviceClient = CreateServiceClient(app, tenantId, integrationId,
            ServiceCredentialAuthenticationHandler.ChannelSessionReportScope);

        var response = await serviceClient.PostAsJsonAsync("/api/internal/channel/session", new
        {
            state = "qr_pending",
            qr = "raw-qr-text-from-baileys",
            atUtc = Now,
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Report_SemClaimsDeTenantOuIntegracao_Retorna401()
    {
        var (app, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Test-Scope", ServiceCredentialAuthenticationHandler.ChannelSessionReportScope);

        var response = await client.PostAsJsonAsync("/api/internal/channel/session", new
        {
            state = "connected",
            phoneNumber = "5511999991234",
            atUtc = Now,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Report_EscopoDeOutroEndpointNaoAutoriza()
    {
        var (app, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var serviceClient = CreateServiceClient(app, Guid.NewGuid(), Guid.NewGuid(),
            ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope);

        var response = await serviceClient.PostAsJsonAsync("/api/internal/channel/session", new
        {
            state = "connected",
            phoneNumber = "5511999991234",
            atUtc = Now,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("invalid_state")]
    [InlineData(null)]
    public async Task Report_EstadoInvalido_Retorna400(string? state)
    {
        var (app, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var serviceClient = CreateServiceClient(app, Guid.NewGuid(), Guid.NewGuid(),
            ServiceCredentialAuthenticationHandler.ChannelSessionReportScope);

        var response = await serviceClient.PostAsJsonAsync("/api/internal/channel/session", new
        {
            state,
            atUtc = Now,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Report_QrPendingSemQr_Retorna400()
    {
        var (app, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var serviceClient = CreateServiceClient(app, Guid.NewGuid(), Guid.NewGuid(),
            ServiceCredentialAuthenticationHandler.ChannelSessionReportScope);

        var response = await serviceClient.PostAsJsonAsync("/api/internal/channel/session", new
        {
            state = "qr_pending",
            atUtc = Now,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Report_PhoneNumberInvalido_Retorna400()
    {
        var (app, _) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var serviceClient = CreateServiceClient(app, Guid.NewGuid(), Guid.NewGuid(),
            ServiceCredentialAuthenticationHandler.ChannelSessionReportScope);

        var response = await serviceClient.PostAsJsonAsync("/api/internal/channel/session", new
        {
            state = "connected",
            phoneNumber = "not-a-phone",
            atUtc = Now,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------------
    // Get (Office)
    // -------------------------------------------------------------------

    [Fact]
    public async Task Get_SemEstadoReportado_RetornaUnknown()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedTenantWithWhatsAppIntegrationAsync(databaseName, ETenantMembershipRole.Owner);
        var browserClient = CreateBrowserClient(app, seed.UserId);

        var response = await browserClient.GetAsync($"/api/tenants/{seed.TenantId}/whatsapp-session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WhatsAppSessionStateResponse>();
        Assert.Equal("unknown", body!.State);
        Assert.Null(body.Qr);
    }

    [Fact]
    public async Task Get_AposReportePeloChannel_RefleteOUltimoEstado()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedTenantWithWhatsAppIntegrationAsync(databaseName, ETenantMembershipRole.Owner);
        var serviceClient = CreateServiceClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelSessionReportScope);
        var browserClient = CreateBrowserClient(app, seed.UserId);

        await serviceClient.PostAsJsonAsync("/api/internal/channel/session", new
        {
            state = "qr_pending",
            qr = "raw-qr-text-from-baileys",
            atUtc = Now,
        });
        var response = await browserClient.GetAsync($"/api/tenants/{seed.TenantId}/whatsapp-session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WhatsAppSessionStateResponse>();
        Assert.Equal("qr_pending", body!.State);
        Assert.Equal("raw-qr-text-from-baileys", body.Qr);
        Assert.Equal(Now.AddSeconds(60), body.QrExpiresAtUtc);
    }

    [Fact]
    public async Task Get_QrExpirado_NaoDevolveQrMasPreservaEstado()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedTenantWithWhatsAppIntegrationAsync(databaseName, ETenantMembershipRole.Owner);
        var serviceClient = CreateServiceClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelSessionReportScope);
        var browserClient = CreateBrowserClient(app, seed.UserId);
        await serviceClient.PostAsJsonAsync("/api/internal/channel/session", new
        {
            state = "qr_pending",
            qr = "raw-qr-text-from-baileys",
            atUtc = Now,
        });

        var timeProvider = (MutableTimeProvider)app.Services.GetRequiredService<TimeProvider>();
        timeProvider.Advance(TimeSpan.FromSeconds(61));
        var response = await browserClient.GetAsync($"/api/tenants/{seed.TenantId}/whatsapp-session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WhatsAppSessionStateResponse>();
        Assert.Equal("qr_pending", body!.State);
        Assert.Null(body.Qr);
    }

    [Fact]
    public async Task Get_AposConectar_DevolvePhoneNumberESemQr()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedTenantWithWhatsAppIntegrationAsync(databaseName, ETenantMembershipRole.Admin);
        var serviceClient = CreateServiceClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelSessionReportScope);
        var browserClient = CreateBrowserClient(app, seed.UserId);
        await serviceClient.PostAsJsonAsync("/api/internal/channel/session", new
        {
            state = "qr_pending",
            qr = "raw-qr-text-from-baileys",
            atUtc = Now,
        });

        await serviceClient.PostAsJsonAsync("/api/internal/channel/session", new
        {
            state = "connected",
            phoneNumber = "5511999991234",
            atUtc = Now.AddSeconds(5),
        });
        var response = await browserClient.GetAsync($"/api/tenants/{seed.TenantId}/whatsapp-session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WhatsAppSessionStateResponse>();
        Assert.Equal("connected", body!.State);
        Assert.Null(body.Qr);
        Assert.Equal("5511999991234", body.PhoneNumber);
    }

    [Fact]
    public async Task Get_UsuarioSemPapelDeGestao_Retorna403()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedTenantWithWhatsAppIntegrationAsync(databaseName, ETenantMembershipRole.Default);
        var browserClient = CreateBrowserClient(app, seed.UserId);

        var response = await browserClient.GetAsync($"/api/tenants/{seed.TenantId}/whatsapp-session");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_IsolamentoPorTenant_NaoVazaEstadoDeOutroTenant()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seedA = await SeedTenantWithWhatsAppIntegrationAsync(databaseName, ETenantMembershipRole.Owner);
        var seedB = await SeedTenantWithWhatsAppIntegrationAsync(databaseName, ETenantMembershipRole.Owner);
        var serviceClientA = CreateServiceClient(app, seedA.TenantId, seedA.IntegrationId,
            ServiceCredentialAuthenticationHandler.ChannelSessionReportScope);
        await serviceClientA.PostAsJsonAsync("/api/internal/channel/session", new
        {
            state = "qr_pending",
            qr = "raw-qr-text-from-baileys",
            atUtc = Now,
        });

        var browserClientB = CreateBrowserClient(app, seedB.UserId);
        var response = await browserClientB.GetAsync($"/api/tenants/{seedB.TenantId}/whatsapp-session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WhatsAppSessionStateResponse>();
        Assert.Equal("unknown", body!.State);
    }

    [Fact]
    public async Task Get_MultiplasIntegracoesSemIntegrationId_Retorna400()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedTenantWithWhatsAppIntegrationAsync(databaseName, ETenantMembershipRole.Owner);
        await using var context = CreateContext(databaseName);
        var secondIntegration = new Integration(Guid.CreateVersion7(), seed.TenantId, Guid.CreateVersion7(), true);
        context.Add(secondIntegration);
        context.ServiceCredentials.Add(new Atua.Api.Domain.Identity.ServiceCredential(
            Guid.CreateVersion7(), seed.TenantId, secondIntegration.Id, Guid.CreateVersion7(),
            "second-integration-claim-hash", ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope));
        await context.SaveChangesAsync();
        var browserClient = CreateBrowserClient(app, seed.UserId);

        var response = await browserClient.GetAsync($"/api/tenants/{seed.TenantId}/whatsapp-session");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_ComIntegrationIdExplicitoDeOutroTenant_Retorna404()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seedA = await SeedTenantWithWhatsAppIntegrationAsync(databaseName, ETenantMembershipRole.Owner);
        var seedB = await SeedTenantWithWhatsAppIntegrationAsync(databaseName, ETenantMembershipRole.Owner);
        var browserClientA = CreateBrowserClient(app, seedA.UserId);

        var response = await browserClientA.GetAsync(
            $"/api/tenants/{seedA.TenantId}/whatsapp-session?integrationId={seedB.IntegrationId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------

    private static async Task<(WebApplication App, string DatabaseName)> CreateApplicationAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var capturedDatabaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AtuaDbContext>(options =>
            options.UseInMemoryDatabase(capturedDatabaseName));
        builder.Services.AddSingleton<TimeProvider>(new MutableTimeProvider(Now));
        builder.Services.AddSingleton<IWhatsAppSessionStateStore, InMemoryWhatsAppSessionStateStore>();
        builder.Services.AddScoped<IReportWhatsAppSessionStateUseCase, ReportWhatsAppSessionStateUseCase>();
        builder.Services.AddScoped<IGetWhatsAppSessionStateUseCase, GetWhatsAppSessionStateUseCase>();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("ChannelSessionReport", policy =>
                policy.AddAuthenticationSchemes("Test")
                    .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ChannelSessionReportScope));
            options.AddPolicy("BrowserSession", policy =>
                policy.AddAuthenticationSchemes("Test").RequireAuthenticatedUser());
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapWhatsAppSessionEndpoints();
        await app.StartAsync();

        return (app, capturedDatabaseName);
    }

    private static HttpClient CreateServiceClient(WebApplication app, Guid tenantId, Guid integrationId, string scope)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Test-TenantId", tenantId.ToString());
        client.DefaultRequestHeaders.Add("Test-IntegrationId", integrationId.ToString());
        client.DefaultRequestHeaders.Add("Test-Scope", scope);
        return client;
    }

    private static HttpClient CreateBrowserClient(WebApplication app, Guid userId)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Test-UserId", userId.ToString());
        return client;
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(
        new DbContextOptionsBuilder<AtuaDbContext>().UseInMemoryDatabase(databaseName).Options);

    private sealed record Seed(Guid TenantId, Guid IntegrationId, Guid UserId);

    /// <summary>
    /// Semeia um tenant com uma integração WhatsApp (identificada pela
    /// credencial de serviço com o escopo <c>channel.command.claim</c> —
    /// ver <see cref="GetWhatsAppSessionStateUseCase"/>) e um usuário com o
    /// papel informado.
    /// </summary>
    private static async Task<Seed> SeedTenantWithWhatsAppIntegrationAsync(
        string databaseName, ETenantMembershipRole role)
    {
        await using var context = CreateContext(databaseName);
        var tenant = new Tenant(Guid.CreateVersion7(), "Natal Refrigeração", "11122233000183",
            "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), "iService", "Midea",
            new Uri("https://provider.example.com"), true);
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var user = new User(Guid.CreateVersion7(), null, $"{Guid.NewGuid():N}@atua.com", "hash");
        context.AddRange(tenant, provider, integration, user);
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, user.Id, role));
        context.ServiceCredentials.Add(new Atua.Api.Domain.Identity.ServiceCredential(
            Guid.CreateVersion7(), tenant.Id, integration.Id, provider.Id,
            $"claim-hash-{integration.Id:N}", ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope));
        await context.SaveChangesAsync();
        return new Seed(tenant.Id, integration.Id, user.Id);
    }

    /// <summary>TimeProvider mutável para simular a expiração do QR nos testes.</summary>
    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class TestAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>();
            if (Request.Headers.TryGetValue("Test-TenantId", out var tenantId))
            {
                claims.Add(new Claim("tenant_id", tenantId.ToString()));
            }

            if (Request.Headers.TryGetValue("Test-IntegrationId", out var integrationId))
            {
                claims.Add(new Claim("integration_id", integrationId.ToString()));
            }

            if (Request.Headers.TryGetValue("Test-Scope", out var scope))
            {
                claims.Add(new Claim("scope", scope.ToString()));
            }

            if (Request.Headers.TryGetValue("Test-UserId", out var userId))
            {
                claims.Add(new Claim("sub", userId.ToString()));
            }

            if (claims.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.Fail("missing claims"));
            }

            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }
}
