using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;
using Atua.Api.Domain.Triagens;
using Atua.Api.Domain.WorkOrders;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de <see cref="TriagemLlmUsageRepository"/> (ADR-048) com o
/// provedor EF Core InMemory — mesmo padrão de <see cref="TriagemRepositoryTests"/>.
/// </summary>
public class TriagemLlmUsageRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordAsync_PersisteUmRegistroDeUsoComOsCamposInformados()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var seed = await SeedAsync(context);
        var repository = new TriagemLlmUsageRepository(context);
        var record = new TriagemLlmUsageRecord(
            seed.TenantId,
            seed.TriagemId,
            "modelo-de-teste",
            InputTokens: 120,
            OutputTokens: 48,
            LatencyMs: 850,
            ETriagemLlmOutcome.Success,
            Now);

        await repository.RecordAsync(record, CancellationToken.None);

        await using var verificationContext = CreateContext(databaseName);
        var persisted = Assert.Single(verificationContext.TriagemLlmUsages);
        Assert.Equal(seed.TenantId, persisted.TenantId);
        Assert.Equal(seed.TriagemId, persisted.TriagemId);
        Assert.Equal("modelo-de-teste", persisted.ModelId);
        Assert.Equal(120, persisted.InputTokens);
        Assert.Equal(48, persisted.OutputTokens);
        Assert.Equal(850, persisted.LatencyMs);
        Assert.Equal(ETriagemLlmOutcome.Success, persisted.Outcome);
        Assert.Equal(Now, persisted.CreatedAtUtc);
    }

    [Fact]
    public async Task RecordAsync_PersisteOutcomesDeFalhaComModelIdVazio()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var seed = await SeedAsync(context);
        var repository = new TriagemLlmUsageRepository(context);
        var record = new TriagemLlmUsageRecord(
            seed.TenantId,
            seed.TriagemId,
            "",
            InputTokens: 0,
            OutputTokens: 0,
            LatencyMs: 0,
            ETriagemLlmOutcome.ProviderUnavailable,
            Now);

        await repository.RecordAsync(record, CancellationToken.None);

        await using var verificationContext = CreateContext(databaseName);
        var persisted = Assert.Single(verificationContext.TriagemLlmUsages);
        Assert.Equal(ETriagemLlmOutcome.ProviderUnavailable, persisted.Outcome);
        Assert.Equal("", persisted.ModelId);
    }

    [Fact]
    public async Task RecordAsync_ChamadasSucessivasGeramRegistrosIndependentes()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var seed = await SeedAsync(context);
        var repository = new TriagemLlmUsageRepository(context);

        await repository.RecordAsync(new TriagemLlmUsageRecord(
            seed.TenantId, seed.TriagemId, "modelo-1", 10, 5, 100, ETriagemLlmOutcome.Success, Now), CancellationToken.None);
        await repository.RecordAsync(new TriagemLlmUsageRecord(
            seed.TenantId, seed.TriagemId, "modelo-1", 20, 8, 200, ETriagemLlmOutcome.InvalidOutput, Now.AddMinutes(1)), CancellationToken.None);

        await using var verificationContext = CreateContext(databaseName);
        Assert.Equal(2, verificationContext.TriagemLlmUsages.Count());
    }

    private sealed record Seed(Guid TenantId, Guid TriagemId);

    private static async Task<Seed> SeedAsync(AtuaDbContext context)
    {
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "11122233000183", "America/Sao_Paulo");
        var provider = new IntegrationProvider(Guid.CreateVersion7(), $"iService-{Guid.NewGuid()}",
            "Fabricante", new Uri("https://provider.example.com"), true);
        var integration = new Integration(Guid.CreateVersion7(), tenant.Id, provider.Id, true);
        var workOrder = new WorkOrder(Guid.NewGuid(), tenant.Id, integration.Id, "OS-EXT-1", "Open", Now);
        var triagem = new Triagem(Guid.NewGuid(), tenant.Id, integration.Id, workOrder.Id, Now);
        context.AddRange(tenant, provider, integration, workOrder, triagem);
        context.Entry(triagem)
            .Property(Atua.Api.Infrastructure.Persistence.TriagemPersistenceSchema.WorkOrderProviderIdShadowPropertyName)
            .CurrentValue = workOrder.WorkOrderProviderId;
        await context.SaveChangesAsync();

        return new Seed(tenant.Id, triagem.Id);
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(databaseName).Options);
}
