using Atua.Api.Application.Triagens;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de <see cref="TriagemEligibleWorkOrderQueryHandler"/> com o
/// provedor EF Core InMemory, isolando a consulta autoritativa de OS usada
/// pelo início idempotente da triagem (ADR-039; arquitetura, seção 3, itens
/// 2/3).
/// </summary>
public class TriagemEligibleWorkOrderQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OsExistenteNaChaveExata_RetornaWorkOrderIdEStatus()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var seed = await SeedAsync(context, status: "assigned");
        var handler = new TriagemEligibleWorkOrderQueryHandler(context);

        var result = await handler.ExecuteAsync(seed.TenantId, seed.IntegrationId, "OS-EXT-1",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(seed.WorkOrderId, result!.WorkOrderId);
        Assert.Equal("assigned", result.Status);
    }

    [Fact]
    public async Task OsInexistente_RetornaNull()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var handler = new TriagemEligibleWorkOrderQueryHandler(context);

        var result = await handler.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), "OS-INEXISTENTE",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task OsDeOutroTenant_NaoEhAlcancada()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var seed = await SeedAsync(context, status: "assigned");
        var handler = new TriagemEligibleWorkOrderQueryHandler(context);

        var result = await handler.ExecuteAsync(Guid.NewGuid(), seed.IntegrationId, "OS-EXT-1",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task OsDeOutraIntegracao_NaoEhAlcancada()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var seed = await SeedAsync(context, status: "assigned");
        var handler = new TriagemEligibleWorkOrderQueryHandler(context);

        var result = await handler.ExecuteAsync(seed.TenantId, Guid.NewGuid(), "OS-EXT-1",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task OsComStatusNaoElegivel_AindaAssimRetornaStatusCruParaOCasoDeUsoDecidir()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var seed = await SeedAsync(context, status: "closed");
        var handler = new TriagemEligibleWorkOrderQueryHandler(context);

        var result = await handler.ExecuteAsync(seed.TenantId, seed.IntegrationId, "OS-EXT-1",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("closed", result!.Status);
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(
        new DbContextOptionsBuilder<AtuaDbContext>().UseInMemoryDatabase(databaseName).Options);

    private sealed record Seed(Guid TenantId, Guid IntegrationId, Guid WorkOrderId);

    private static async Task<Seed> SeedAsync(AtuaDbContext context, string status)
    {
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "11122233000183", "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), $"iService-{Guid.NewGuid()}",
            "Fabricante", new Uri("https://provider.example.com"), true);
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var workOrder = new WorkOrder(Guid.NewGuid(), tenant.Id, integration.Id, "OS-EXT-1", status, Now);
        context.AddRange(tenant, provider, integration, workOrder);
        await context.SaveChangesAsync();

        return new Seed(tenant.Id, integration.Id, workOrder.Id);
    }
}
