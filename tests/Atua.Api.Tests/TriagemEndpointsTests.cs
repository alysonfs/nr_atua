using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Triagens;
using Atua.Api.Domain.Tenants;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atua.Api.Tests;

/// <summary>
/// Testes HTTP de <c>POST /api/internal/triagens/start</c> (ADR-039;
/// arquitetura, seção 3, itens 2/3). Usa <see cref="TestServer"/> com um
/// esquema de autenticação de teste que simula os claims produzidos por
/// <see cref="ServiceCredentialAuthenticationHandler"/> a partir de headers,
/// permitindo testar os caminhos de autorização sem um banco real de
/// credenciais.
/// </summary>
public class TriagemEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PrimeiraChamadaComOsElegivel_Retorna201ComLocationEBody()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedWorkOrderAsync(databaseName, status: "assigned");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId, ServiceCredentialAuthenticationHandler.TriageStartScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/start",
            new StartTriagemRequest { WorkOrderProviderId = "OS-EXT-1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<StartTriagemResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Created);
        // Canal fake padrão responde com sucesso — o endpoint já reflete o
        // template enviado (ADR-039/arquitetura seção 5.3), não mais apenas
        // AguardandoEnvio.
        Assert.Equal("ContatoIniciado", body.Status);
        Assert.Contains(body.TriagemId.ToString(), response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task RepeticaoDoMesmoGatilho_Retorna200ComEstadoAtualECreatedFalse()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedWorkOrderAsync(databaseName, status: "assigned");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId, ServiceCredentialAuthenticationHandler.TriageStartScope);
        var request = new StartTriagemRequest { WorkOrderProviderId = "OS-EXT-1" };

        var first = await client.PostAsJsonAsync("/api/internal/triagens/start", request);
        var second = await client.PostAsJsonAsync("/api/internal/triagens/start", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<StartTriagemResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<StartTriagemResponse>();
        Assert.False(secondBody!.Created);
        Assert.Equal(firstBody!.TriagemId, secondBody.TriagemId);
    }

    [Fact]
    public async Task OsInexistente_Retorna404()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedWorkOrderAsync(databaseName, status: "assigned");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId, ServiceCredentialAuthenticationHandler.TriageStartScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/start",
            new StartTriagemRequest { WorkOrderProviderId = "OS-NAO-EXISTE" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OsComStatusNaoElegivel_Retorna409ProblemDetailsSemCriarTriagem()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedWorkOrderAsync(databaseName, status: "closed");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId, ServiceCredentialAuthenticationHandler.TriageStartScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/start",
            new StartTriagemRequest { WorkOrderProviderId = "OS-EXT-1" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem!.Status);

        await using var context = CreateContext(databaseName);
        Assert.Empty(context.Triagens);
    }

    [Fact]
    public async Task ClaimsDeTenantOuIntegracaoAusentes_Retorna401()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        _ = databaseName;
        // Escopo correto (passa a policy), porém sem tenant_id/integration_id.
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Test-Scope", ServiceCredentialAuthenticationHandler.TriageStartScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/start",
            new StartTriagemRequest { WorkOrderProviderId = "OS-EXT-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EscopoCollectorExistente_NaoAutorizaARota()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedWorkOrderAsync(databaseName, status: "assigned");
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId,
            ServiceCredentialAuthenticationHandler.EligibilityScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/start",
            new StartTriagemRequest { WorkOrderProviderId = "OS-EXT-1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void StartTriagemRequest_AceitaSomenteWorkOrderProviderIdExterno()
    {
        // Garante, por construção do tipo, que tenant/integração/WorkOrderId
        // interno nunca podem ser aceitos pelo corpo da requisição — a
        // única fonte é a credencial de serviço autenticada.
        var properties = typeof(StartTriagemRequest).GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(["WorkOrderProviderId"], properties);
    }

    [Fact]
    public async Task CanalRetornaFalhaPermanente_Retorna201ComStatusFalhaDeContatoEPersisteNoBanco()
    {
        // Regressão do endpoint com persistência real via EF InMemory: prova
        // que o resultado estruturado do canal (falha definitiva) é
        // persistido como transição de estado através do contrato HTTP
        // existente, sem alterar o contrato (ainda 201 + corpo com Status).
        var channel = new FakeTriagemChannel();
        var (app, databaseName) = await CreateApplicationAsync(channel);
        await using var appDisposable = app;
        var seed = await SeedWorkOrderAsync(databaseName, status: "assigned");
        var key = new TriagemIdempotencyKey(seed.TenantId, seed.IntegrationId, "OS-EXT-1");
        channel.ConfigureResult(key.ToString(),
            TriagemChannelSendResult.PermanentFailure("template-rejeitado", "Número de contato inválido."));
        var client = CreateClient(app, seed.TenantId, seed.IntegrationId, ServiceCredentialAuthenticationHandler.TriageStartScope);

        var response = await client.PostAsJsonAsync("/api/internal/triagens/start",
            new StartTriagemRequest { WorkOrderProviderId = "OS-EXT-1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StartTriagemResponse>();
        Assert.NotNull(body);
        Assert.Equal("FalhaDeContato", body!.Status);

        await using var context = CreateContext(databaseName);
        var stored = await context.Triagens.SingleAsync(triagem => triagem.Id == body.TriagemId);
        Assert.Equal(ETriagemStatus.FalhaDeContato, stored.Status);
    }

    // -------------------------------------------------------------------
    // Infraestrutura de teste
    // -------------------------------------------------------------------

    private static async Task<(WebApplication App, string DatabaseName)> CreateApplicationAsync(
        ITriagemChannel? channel = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var capturedDatabaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AtuaDbContext>(options =>
            options.UseInMemoryDatabase(capturedDatabaseName));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ITriagemRepository, TriagemRepository>();
        builder.Services.AddScoped<ITriagemEligibleWorkOrderQuery, TriagemEligibleWorkOrderQueryHandler>();
        builder.Services.AddSingleton<ITriagemChannel>(channel ?? new FakeTriagemChannel());
        builder.Services.AddScoped<StartTriagemUseCase>();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("TriageStart", policy =>
                policy.AddAuthenticationSchemes("Test")
                    .RequireClaim("scope", ServiceCredentialAuthenticationHandler.TriageStartScope));
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapTriagemEndpoints();
        await app.StartAsync();
        return (app, capturedDatabaseName);
    }

    private static HttpClient CreateClient(WebApplication app, Guid tenantId, Guid integrationId, string scope)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Test-TenantId", tenantId.ToString());
        client.DefaultRequestHeaders.Add("Test-IntegrationId", integrationId.ToString());
        client.DefaultRequestHeaders.Add("Test-Scope", scope);
        return client;
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(
        new DbContextOptionsBuilder<AtuaDbContext>().UseInMemoryDatabase(databaseName).Options);

    private sealed record Seed(Guid TenantId, Guid IntegrationId, Guid WorkOrderId);

    private static async Task<Seed> SeedWorkOrderAsync(string databaseName, string status)
    {
        await using var context = CreateContext(databaseName);
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "11122233000183", "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), $"iService-{Guid.NewGuid()}",
            "Fabricante", new Uri("https://provider.example.com"), true);
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var workOrder = new WorkOrder(Guid.NewGuid(), tenant.Id, integration.Id, "OS-EXT-1", status, Now);
        context.AddRange(tenant, provider, integration, workOrder);
        await context.SaveChangesAsync();

        return new Seed(tenant.Id, integration.Id, workOrder.Id);
    }

    /// <summary>
    /// Simula os claims produzidos por <see cref="ServiceCredentialAuthenticationHandler"/>
    /// a partir de headers de teste, sem depender de um banco real de
    /// credenciais. Sempre autentica com sucesso — a ausência de claims
    /// (tenant_id/integration_id) é usada para testar o caminho 401 dentro
    /// do handler, e a ausência/divergência do claim <c>scope</c> é usada
    /// para testar o caminho de autorização (403) da policy.
    /// </summary>
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

            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }
}
