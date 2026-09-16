using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atua.Api.Application.WorkOrders;
using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Domain.Tenants;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atua.Api.Tests;

public class WorkOrderEndpointsTests
{
    [Fact]
    public async Task GetMonthlySummaryRetorna403QuandoUsuarioNaoEMembroDoTenant()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var tenantId = await SeedTenantAsync(databaseName);
        var client = CreateAuthenticatedClient(app, Guid.CreateVersion7());

        var response = await client.GetAsync($"/api/tenants/{tenantId}/work-orders/summary?month=2026-09");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMonthlySummaryRetorna400QuandoMesAusenteOuInvalido()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var userId = Guid.CreateVersion7();
        var tenantId = await SeedTenantWithMembershipAsync(databaseName, userId);
        var client = CreateAuthenticatedClient(app, userId);

        var responseSemMes = await client.GetAsync($"/api/tenants/{tenantId}/work-orders/summary");
        var responseMesInvalido =
            await client.GetAsync($"/api/tenants/{tenantId}/work-orders/summary?month=2026-9");

        Assert.Equal(HttpStatusCode.BadRequest, responseSemMes.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, responseMesInvalido.StatusCode);
    }

    [Fact]
    public async Task GetMonthlySummaryReconstroiStatusVigentePorDiaAPartirDoHistorico()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var userId = Guid.CreateVersion7();
        var tenantId = await SeedTenantWithMembershipAsync(databaseName, userId, "America/Sao_Paulo");

        // OS criada em 2026-09-01 como "Novo"; muda para "Em Andamento" em
        // 2026-09-05. Nos dias 1-4 deve contar como "Novo", do dia 5 em
        // diante como "Em Andamento" (RN de reconstrução por dia).
        var workOrderId = Guid.CreateVersion7();
        await using (var context = CreateContext(databaseName))
        {
            var createdAt = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.FromHours(-3));
            var changedAt = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(-3));
            var workOrder = new WorkOrder(workOrderId, tenantId, "OS-1", "Novo", createdAt);
            workOrder.UpdateStatus("Em Andamento", changedAt);
            context.WorkOrders.Add(workOrder);
            context.WorkOrderHistories.Add(new WorkOrderHistory(Guid.CreateVersion7(), workOrderId,
                Guid.CreateVersion7(), tenantId, "OS-1", "Novo", createdAt));
            context.WorkOrderHistories.Add(new WorkOrderHistory(Guid.CreateVersion7(), workOrderId,
                Guid.CreateVersion7(), tenantId, "OS-1", "Em Andamento", changedAt));
            await context.SaveChangesAsync();
        }

        var client = CreateAuthenticatedClient(app, userId);
        var response = await client.GetAsync($"/api/tenants/{tenantId}/work-orders/summary?month=2026-09");
        var body = await response.Content.ReadFromJsonAsync<WorkOrderMonthlySummaryResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-09", body!.Month);

        var novo = body.Statuses.Single(status => status.Status == "Novo");
        var emAndamento = body.Statuses.Single(status => status.Status == "Em Andamento");

        Assert.Equal(4, novo.Total);
        Assert.Equal(1, novo.DailyCounts[0]);
        Assert.Equal(1, novo.DailyCounts[3]);
        Assert.Equal(0, novo.DailyCounts[4]);

        Assert.Equal(1, emAndamento.DailyCounts[4]);
        Assert.Equal(26, emAndamento.Total);
    }

    [Fact]
    public async Task GetListByStatusRetorna403QuandoUsuarioNaoEMembroDoTenant()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var tenantId = await SeedTenantAsync(databaseName);
        var client = CreateAuthenticatedClient(app, Guid.CreateVersion7());

        var response = await client.GetAsync($"/api/tenants/{tenantId}/work-orders?status=Novo");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetListByStatusFiltraPorStatusEOrdenaPorAtualizacaoDesc()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var userId = Guid.CreateVersion7();
        var tenantId = await SeedTenantWithMembershipAsync(databaseName, userId);

        await using (var context = CreateContext(databaseName))
        {
            var older = new WorkOrder(Guid.CreateVersion7(), tenantId, "OS-1", "Novo",
                DateTimeOffset.UtcNow.AddDays(-2));
            var newer = new WorkOrder(Guid.CreateVersion7(), tenantId, "OS-2", "Novo",
                DateTimeOffset.UtcNow.AddDays(-1));
            newer.Touch(DateTimeOffset.UtcNow);
            var otherStatus = new WorkOrder(Guid.CreateVersion7(), tenantId, "OS-3", "Concluído",
                DateTimeOffset.UtcNow);
            context.WorkOrders.AddRange(older, newer, otherStatus);
            await context.SaveChangesAsync();
        }

        var client = CreateAuthenticatedClient(app, userId);
        var response = await client.GetAsync($"/api/tenants/{tenantId}/work-orders?status=Novo");
        var body = await response.Content.ReadFromJsonAsync<WorkOrderListResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, body!.TotalCount);
        Assert.Equal("OS-2", body.Items[0].ProviderId);
        Assert.Equal("OS-1", body.Items[1].ProviderId);
    }

    [Fact]
    public async Task GetStatusSummaryRetorna403QuandoUsuarioNaoEMembroDoTenant()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var tenantId = await SeedTenantAsync(databaseName);
        var client = CreateAuthenticatedClient(app, Guid.CreateVersion7());

        var response = await client.GetAsync($"/api/tenants/{tenantId}/work-orders/status-summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetStatusSummaryContaOSPorStatusAtualSemRecorteDeMes()
    {
        var (app, databaseName) = await CreateApplicationAsync();
        await using var appDisposable = app;
        var userId = Guid.CreateVersion7();
        var tenantId = await SeedTenantWithMembershipAsync(databaseName, userId);

        await using (var context = CreateContext(databaseName))
        {
            context.WorkOrders.AddRange(
                new WorkOrder(Guid.CreateVersion7(), tenantId, "OS-1", "pending", DateTimeOffset.UtcNow.AddMonths(-3)),
                new WorkOrder(Guid.CreateVersion7(), tenantId, "OS-2", "pending", DateTimeOffset.UtcNow.AddMonths(-2)),
                new WorkOrder(Guid.CreateVersion7(), tenantId, "OS-3", "closed", DateTimeOffset.UtcNow.AddMonths(-1)));
            await context.SaveChangesAsync();
        }

        var client = CreateAuthenticatedClient(app, userId);
        var response = await client.GetAsync($"/api/tenants/{tenantId}/work-orders/status-summary");
        var body = await response.Content.ReadFromJsonAsync<WorkOrderStatusSummaryResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, body!.Statuses.Single(status => status.Status == "pending").Total);
        Assert.Equal(1, body.Statuses.Single(status => status.Status == "closed").Total);
    }

    private static async Task<Guid> SeedTenantAsync(string databaseName)
    {
        var tenantId = Guid.CreateVersion7();
        await using var context = CreateContext(databaseName);
        context.Tenants.Add(new Tenant(tenantId, "Atua", "11122233000183", "America/Sao_Paulo"));
        await context.SaveChangesAsync();
        return tenantId;
    }

    private static async Task<Guid> SeedTenantWithMembershipAsync(string databaseName, Guid userId,
        string timeZoneId = "America/Sao_Paulo")
    {
        var tenantId = Guid.CreateVersion7();
        await using var context = CreateContext(databaseName);
        context.Tenants.Add(new Tenant(tenantId, "Atua", "11122233000183", timeZoneId));
        context.TenantMemberships.Add(new TenantMembership(tenantId, userId, ETenantMembershipRole.Owner));
        await context.SaveChangesAsync();
        return tenantId;
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
        builder.Services.AddScoped<IWorkOrderMonthlySummaryQuery, WorkOrderMonthlySummaryQueryHandler>();
        builder.Services.AddScoped<IWorkOrderListByStatusQuery, WorkOrderListByStatusQueryHandler>();
        builder.Services.AddScoped<IWorkOrderStatusSummaryQuery, WorkOrderStatusSummaryQueryHandler>();
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
        app.MapWorkOrderEndpoints();
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
