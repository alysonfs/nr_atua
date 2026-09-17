using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atua.Api.Application.Integrations;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
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
/// Contrato Office de RF-025: GET/PUT
/// .../integrations/{integrationId}/recurrent-collection-interval.
/// </summary>
public class RecurrentCollectionIntervalEndpointsTests
{
    [Fact]
    public async Task GetRetornaValorPadraoDe15Minutos()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName);
        var client = CreateClient(app, seed.OwnerId);

        var response = await client.GetAsync(RouteFor(seed));
        var body = await response.Content.ReadFromJsonAsync<RecurrentCollectionIntervalView>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(15, body!.RecurrentCollectionIntervalMinutes);
    }

    [Fact]
    public async Task PutAtualizaIntervaloERetornaValorAtualizado()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName);
        var client = CreateClient(app, seed.OwnerId);

        var response = await client.PutAsJsonAsync(RouteFor(seed),
            new SetRecurrentCollectionIntervalRequest(30));
        var body = await response.Content.ReadFromJsonAsync<RecurrentCollectionIntervalView>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(30, body!.RecurrentCollectionIntervalMinutes);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    public async Task PutForaDosLimitesRetorna400(int minutes)
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName);
        var client = CreateClient(app, seed.OwnerId);

        var response = await client.PutAsJsonAsync(RouteFor(seed),
            new SetRecurrentCollectionIntervalRequest(minutes));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_recurrent_collection_interval", raw);
    }

    [Fact]
    public async Task PutRetorna403ParaUsuarioSemMembership()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var seed = await SeedAsync(databaseName);
        var client = CreateClient(app, Guid.CreateVersion7());

        var response = await client.PutAsJsonAsync(RouteFor(seed),
            new SetRecurrentCollectionIntervalRequest(30));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var context = CreateContext(databaseName);
        var integration = await context.Integrations.SingleAsync(item => item.Id == seed.IntegrationId);
        Assert.Equal(15, integration.RecurrentCollectionIntervalMinutes);
    }

    private static string RouteFor(Seed seed) =>
        $"/api/tenants/{seed.TenantId}/integrations/{seed.IntegrationId}/recurrent-collection-interval";

    private sealed record Seed(Guid TenantId, Guid IntegrationId, Guid OwnerId);

    private static async Task<Seed> SeedAsync(string databaseName)
    {
        await using var context = CreateContext(databaseName);
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "11122233000183", "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), "iService", "Fabricante",
            new Uri("https://provider.example.com"), true);
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var owner = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        context.AddRange(tenant, provider, integration, owner);
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, owner.Id, ETenantMembershipRole.Owner));
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
        builder.Services.AddScoped<RecurrentCollectionIntervalService>();
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
        app.MapRecurrentCollectionIntervalEndpoints();
        await app.StartAsync();
        return (app, capturedDatabaseName);
    }

    private static HttpClient CreateClient(WebApplication app, Guid userId)
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
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, "Test")));
        }
    }
}
