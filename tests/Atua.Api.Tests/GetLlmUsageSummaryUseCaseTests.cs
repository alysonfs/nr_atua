using Atua.Api.Application.LlmUsage;
using Atua.Api.Application.LlmUsage.Contracts;
using Atua.Api.Domain.Tenants;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

/// <summary>
/// Testes do relatório de consumo/custo de LLM por tenant para o Office
/// (ADR-048, pendência "Relatório de consumo por tenant no Office"). Cobre
/// agregação (totais/tenant/dia/modelo), custo estimado por modelo,
/// isolamento por tenant (sem papel de administrador de plataforma — ver
/// <see cref="IGetLlmUsageSummaryUseCase"/>) e validação do período.
/// </summary>
public class GetLlmUsageSummaryUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 15, 0, 0, TimeSpan.Zero);
    private const string HaikuModelId = "global.anthropic.claude-haiku-4-5-20251001-v1:0";

    [Fact]
    public async Task Execute_AgregaTotaisTenantDiaEModeloDentroDoPeriodo()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);

            // 22/10 em America/Sao_Paulo (UTC-3): 23/10 01:00 UTC = 22/10 22:00 SP.
            AddUsage(context, tenantId, HaikuModelId, 1000, 2000, 500, ETriagemLlmOutcome.Success,
                new DateTimeOffset(2026, 10, 23, 1, 0, 0, TimeSpan.Zero));

            // 23/10 em America/Sao_Paulo: 23/10 15:00 UTC = 23/10 12:00 SP.
            AddUsage(context, tenantId, HaikuModelId, 3000, 4000, 1500, ETriagemLlmOutcome.InvalidOutput,
                new DateTimeOffset(2026, 10, 23, 15, 0, 0, TimeSpan.Zero));

            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateUseCase(assertContext, Now);

        var result = await useCase.ExecuteAsync(
            new LlmUsageSummaryQuery(userId, new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 23)),
            CancellationToken.None);

        Assert.Equal(ELlmUsageSummaryOutcome.Success, result.Outcome);
        Assert.Equal(new DateOnly(2026, 10, 22), result.From);
        Assert.Equal(new DateOnly(2026, 10, 23), result.To);

        Assert.Equal(2, result.Totals!.Calls);
        Assert.Equal(4000, result.Totals.InputTokens);
        Assert.Equal(6000, result.Totals.OutputTokens);
        Assert.Equal(1, result.Totals.FailedCalls);
        Assert.Equal(1000, result.Totals.AvgLatencyMs);

        var tenant = Assert.Single(result.Tenants!);
        Assert.Equal(tenantId, tenant.TenantId);
        Assert.Equal(2, tenant.Calls);
        Assert.Equal(1, tenant.FailedCalls);

        Assert.Equal(2, result.Daily!.Count);
        var dia22 = result.Daily!.Single(day => day.Date == new DateOnly(2026, 10, 22));
        var dia23 = result.Daily!.Single(day => day.Date == new DateOnly(2026, 10, 23));
        Assert.Equal(1, dia22.Calls);
        Assert.Equal(1000, dia22.InputTokens);
        Assert.Equal(1, dia23.Calls);
        Assert.Equal(3000, dia23.InputTokens);

        var modelo = Assert.Single(result.Models!);
        Assert.Equal(HaikuModelId, modelo.ModelId);
        Assert.Equal(2, modelo.Calls);
    }

    [Fact]
    public async Task Execute_CustoEstimado_UsaPrecoDefaultDoModeloConhecido()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            // 1_000_000 tokens de entrada (USD 1,00) + 1_000_000 de saída (USD 5,00) = USD 6,00.
            AddUsage(context, tenantId, HaikuModelId, 1_000_000, 1_000_000, 100, ETriagemLlmOutcome.Success, Now);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateUseCase(assertContext, Now);

        var result = await useCase.ExecuteAsync(
            new LlmUsageSummaryQuery(userId, new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 23)),
            CancellationToken.None);

        Assert.Equal(ELlmUsageSummaryOutcome.Success, result.Outcome);
        Assert.Equal(6.00m, result.Totals!.EstimatedCost);
    }

    [Fact]
    public async Task Execute_CustoEstimado_ConfiguracaoSobrescreveDefault()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            AddUsage(context, tenantId, HaikuModelId, 1_000_000, 1_000_000, 100, ETriagemLlmOutcome.Success, Now);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var pricingCatalog = new TriagemLlmPricingCatalog([new TriagemLlmModelPricing(HaikuModelId, 2.00m, 10.00m)]);
        var useCase = CreateUseCase(assertContext, Now, pricingCatalog);

        var result = await useCase.ExecuteAsync(
            new LlmUsageSummaryQuery(userId, new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 23)),
            CancellationToken.None);

        Assert.Equal(12.00m, result.Totals!.EstimatedCost);
    }

    [Fact]
    public async Task Execute_ModeloSemPrecoConfiguradoOuDefault_CustaZeroENaoQuebra()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            AddUsage(context, tenantId, "modelo-desconhecido", 1_000_000, 1_000_000, 100,
                ETriagemLlmOutcome.Success, Now);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateUseCase(assertContext, Now);

        var result = await useCase.ExecuteAsync(
            new LlmUsageSummaryQuery(userId, new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 23)),
            CancellationToken.None);

        Assert.Equal(ELlmUsageSummaryOutcome.Success, result.Outcome);
        Assert.Equal(0m, result.Totals!.EstimatedCost);
        var modelo = Assert.Single(result.Models!);
        Assert.Equal(0m, modelo.EstimatedCost);
    }

    [Fact]
    public async Task Execute_UsuarioSoEnxergaOsPropriosTenants()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        Guid tenantId;

        await using (var context = CreateContext(databaseName))
        {
            tenantId = await SeedTenantWithMembershipAsync(context, userId);
            AddUsage(context, tenantId, HaikuModelId, 100, 200, 10, ETriagemLlmOutcome.Success, Now);

            // Outro tenant, sem membership do usuário — não deve aparecer no relatório.
            var outroTenantId = await SeedTenantWithMembershipAsync(context, Guid.CreateVersion7());
            AddUsage(context, outroTenantId, HaikuModelId, 999, 999, 10, ETriagemLlmOutcome.Success, Now);

            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateUseCase(assertContext, Now);

        var result = await useCase.ExecuteAsync(
            new LlmUsageSummaryQuery(userId, new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 23)),
            CancellationToken.None);

        Assert.Equal(ELlmUsageSummaryOutcome.Success, result.Outcome);
        var tenant = Assert.Single(result.Tenants!);
        Assert.Equal(tenantId, tenant.TenantId);
        Assert.Equal(1, result.Totals!.Calls);
        Assert.Equal(100, result.Totals.InputTokens);
    }

    [Fact]
    public async Task Execute_UsuarioSemNenhumTenantMembership_RetornaForbidden()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var useCase = CreateUseCase(context, Now);

        var result = await useCase.ExecuteAsync(new LlmUsageSummaryQuery(Guid.CreateVersion7(), null, null),
            CancellationToken.None);

        Assert.Equal(ELlmUsageSummaryOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Execute_FromMaiorQueTo_RetornaInvalidDateRange()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();

        await using (var context = CreateContext(databaseName))
        {
            await SeedTenantWithMembershipAsync(context, userId);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateUseCase(assertContext, Now);

        var result = await useCase.ExecuteAsync(
            new LlmUsageSummaryQuery(userId, new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 1)),
            CancellationToken.None);

        Assert.Equal(ELlmUsageSummaryOutcome.InvalidDateRange, result.Outcome);
    }

    [Fact]
    public async Task Execute_IntervaloMaiorQue366Dias_RetornaInvalidDateRange()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();

        await using (var context = CreateContext(databaseName))
        {
            await SeedTenantWithMembershipAsync(context, userId);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateUseCase(assertContext, Now);

        var result = await useCase.ExecuteAsync(
            new LlmUsageSummaryQuery(userId, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 2)),
            CancellationToken.None);

        Assert.Equal(ELlmUsageSummaryOutcome.InvalidDateRange, result.Outcome);
    }

    [Fact]
    public async Task Execute_SemFromETo_UsaUltimosTrintaDiasEmSaoPaulo()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();

        await using (var context = CreateContext(databaseName))
        {
            await SeedTenantWithMembershipAsync(context, userId);
            await context.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        var useCase = CreateUseCase(assertContext, Now);

        var result = await useCase.ExecuteAsync(new LlmUsageSummaryQuery(userId, null, null), CancellationToken.None);

        Assert.Equal(ELlmUsageSummaryOutcome.Success, result.Outcome);
        // Now = 2026-10-23T15:00:00Z = 2026-10-23T12:00:00 em America/Sao_Paulo (UTC-3).
        Assert.Equal(new DateOnly(2026, 10, 23), result.To);
        Assert.Equal(new DateOnly(2026, 9, 24), result.From);
        Assert.Equal(30, result.To.DayNumber - result.From.DayNumber + 1);
    }

    [Fact]
    public async Task Execute_EnviaLimitesUtcParaPostgresPreservandoDiaLocal()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var userId = Guid.CreateVersion7();
        await SeedTenantWithMembershipAsync(context, userId);
        var aggregateQuery = new UtcRangeCheckingAggregateQuery();
        var useCase = new GetLlmUsageSummaryUseCase(context, aggregateQuery,
            new TriagemLlmPricingCatalog(), new FakeTimeProvider(Now));

        var result = await useCase.ExecuteAsync(new LlmUsageSummaryQuery(userId,
            new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 23)), CancellationToken.None);

        Assert.Equal(ELlmUsageSummaryOutcome.Success, result.Outcome);
        Assert.True(aggregateQuery.Executed);
    }

    private sealed class UtcRangeCheckingAggregateQuery : ITriagemLlmUsageAggregateQuery
    {
        public bool Executed { get; private set; }

        public Task<IReadOnlyList<TriagemLlmUsageAggregateRow>> ExecuteAsync(
            IReadOnlyCollection<Guid> tenantIds, DateTimeOffset fromUtcInclusive,
            DateTimeOffset toUtcExclusive, CancellationToken cancellationToken)
        {
            Executed = true;
            Assert.Equal(TimeSpan.Zero, fromUtcInclusive.Offset);
            Assert.Equal(TimeSpan.Zero, toUtcExclusive.Offset);
            Assert.Equal(new DateTimeOffset(2026, 10, 23, 3, 0, 0, TimeSpan.Zero), fromUtcInclusive);
            Assert.Equal(new DateTimeOffset(2026, 10, 24, 3, 0, 0, TimeSpan.Zero), toUtcExclusive);
            return Task.FromResult<IReadOnlyList<TriagemLlmUsageAggregateRow>>([]);
        }
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static GetLlmUsageSummaryUseCase CreateUseCase(AtuaDbContext context, DateTimeOffset now,
        TriagemLlmPricingCatalog? pricingCatalog = null) =>
        new(context, new TriagemLlmUsageAggregateQueryHandler(context), pricingCatalog ?? new TriagemLlmPricingCatalog(),
            new FakeTimeProvider(now));

    private static void AddUsage(AtuaDbContext context, Guid tenantId, string modelId, int inputTokens,
        int outputTokens, long latencyMs, ETriagemLlmOutcome outcome, DateTimeOffset createdAtUtc) =>
        context.TriagemLlmUsages.Add(new TriagemLlmUsage(Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(),
            modelId, inputTokens, outputTokens, latencyMs, outcome, createdAtUtc));

    private static async Task<Guid> SeedTenantWithMembershipAsync(AtuaDbContext context, Guid userId,
        ETenantMembershipRole role = ETenantMembershipRole.Owner)
    {
        var tenantId = Guid.CreateVersion7();
        context.Tenants.Add(new Tenant(tenantId, "Atua", "11122233000183", "America/Sao_Paulo"));
        context.TenantMemberships.Add(new TenantMembership(tenantId, userId, role));
        await context.SaveChangesAsync();
        return tenantId;
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(
        new DbContextOptionsBuilder<AtuaDbContext>().UseInMemoryDatabase(databaseName).Options);
}
