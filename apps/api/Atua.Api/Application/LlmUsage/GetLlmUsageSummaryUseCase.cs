using Atua.Api.Application.LlmUsage.Contracts;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.LlmUsage;

/// <summary>
/// Implementação de <see cref="IGetLlmUsageSummaryUseCase"/> — relatório de
/// consumo/custo de LLM por tenant para o Office (ADR-048). A agregação em
/// si (por tenant, modelo e dia local) é feita no banco por
/// <see cref="ITriagemLlmUsageAggregateQuery"/>; este caso de uso só resolve
/// o escopo de tenants do usuário, valida o período e faz os somatórios
/// finais (tenant/dia/modelo/totais) a partir dos grupos já agregados — um
/// conjunto pequeno e limitado, nunca as linhas brutas de uso.
/// </summary>
public sealed class GetLlmUsageSummaryUseCase(
    AtuaDbContext dbContext,
    ITriagemLlmUsageAggregateQuery aggregateQuery,
    TriagemLlmPricingCatalog pricingCatalog,
    TimeProvider timeProvider) : IGetLlmUsageSummaryUseCase
{
    // Brasil não observa horário de verão desde 2019: o deslocamento de
    // America/Sao_Paulo é sempre UTC-3. A tabela triagem_llm_usage só
    // existe a partir de 2026-10-23 (migration AddTriagemLlmUsage), então
    // não há dados anteriores à mudança que exigiriam o deslocamento
    // histórico (-02:00/-03:00 variável). Simplificação deliberada; se o
    // Brasil voltar a adotar DST, revisar para usar o fuso IANA completo.
    private static readonly TimeSpan SaoPauloOffset = TimeSpan.FromHours(-3);

    private const int DefaultRangeDays = 30;
    private const int MaxRangeDays = 366;

    public async Task<LlmUsageSummaryResult> ExecuteAsync(LlmUsageSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var tenantIds = await dbContext.TenantMemberships.AsNoTracking()
            .Where(membership => membership.UserId == query.UserId)
            .Select(membership => membership.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tenantIds.Count == 0) return LlmUsageSummaryResult.Failure(ELlmUsageSummaryOutcome.Forbidden);

        var todaySp = DateOnly.FromDateTime((timeProvider.GetUtcNow() + SaoPauloOffset).DateTime);
        var to = query.To ?? todaySp;
        var from = query.From ?? to.AddDays(-(DefaultRangeDays - 1));

        if (from > to) return LlmUsageSummaryResult.Failure(ELlmUsageSummaryOutcome.InvalidDateRange);
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            return LlmUsageSummaryResult.Failure(ELlmUsageSummaryOutcome.InvalidDateRange);
        }

        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), SaoPauloOffset);
        var toUtcExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), SaoPauloOffset);

        var rows = await aggregateQuery.ExecuteAsync(tenantIds, fromUtc, toUtcExclusive, cancellationToken);

        var tenantNames = await dbContext.Tenants.AsNoTracking()
            .Where(tenant => tenantIds.Contains(tenant.Id))
            .ToDictionaryAsync(tenant => tenant.Id, tenant => tenant.Name, cancellationToken);

        var totals = BuildTotals(rows);

        var tenants = rows.GroupBy(row => row.TenantId)
            .Select(group => BuildTenantBreakdown(group.Key,
                tenantNames.TryGetValue(group.Key, out var name) ? name : group.Key.ToString(), group))
            .OrderByDescending(item => item.EstimatedCost)
            .ToList();

        var models = rows.GroupBy(row => row.ModelId)
            .Select(group => BuildModelBreakdown(group.Key, group))
            .OrderByDescending(item => item.EstimatedCost)
            .ToList();

        var daily = BuildDailySeries(rows, from, to);

        return new LlmUsageSummaryResult(ELlmUsageSummaryOutcome.Success, from, to, totals, tenants, daily, models);
    }

    private LlmUsageTotals BuildTotals(IReadOnlyList<TriagemLlmUsageAggregateRow> rows)
    {
        var calls = rows.Sum(row => row.Calls);
        var inputTokens = rows.Sum(row => row.InputTokens);
        var outputTokens = rows.Sum(row => row.OutputTokens);
        var totalLatencyMs = rows.Sum(row => row.TotalLatencyMs);
        var failedCalls = rows.Sum(row => row.FailedCalls);
        var estimatedCost = EstimateCost(rows);
        var avgLatencyMs = calls == 0 ? 0 : totalLatencyMs / calls;

        return new LlmUsageTotals(calls, inputTokens, outputTokens, estimatedCost, avgLatencyMs, failedCalls);
    }

    private LlmUsageTenantBreakdown BuildTenantBreakdown(Guid tenantId, string tenantName,
        IEnumerable<TriagemLlmUsageAggregateRow> group)
    {
        var rows = group.ToList();
        var calls = rows.Sum(row => row.Calls);
        var inputTokens = rows.Sum(row => row.InputTokens);
        var outputTokens = rows.Sum(row => row.OutputTokens);
        var totalLatencyMs = rows.Sum(row => row.TotalLatencyMs);
        var failedCalls = rows.Sum(row => row.FailedCalls);
        var estimatedCost = EstimateCost(rows);
        var avgLatencyMs = calls == 0 ? 0 : totalLatencyMs / calls;

        return new LlmUsageTenantBreakdown(tenantId, tenantName, calls, inputTokens, outputTokens, estimatedCost,
            avgLatencyMs, failedCalls);
    }

    private LlmUsageModelBreakdown BuildModelBreakdown(string modelId, IEnumerable<TriagemLlmUsageAggregateRow> group)
    {
        var rows = group.ToList();
        var calls = rows.Sum(row => row.Calls);
        var inputTokens = rows.Sum(row => row.InputTokens);
        var outputTokens = rows.Sum(row => row.OutputTokens);
        var estimatedCost = EstimateCost(rows);

        return new LlmUsageModelBreakdown(modelId, calls, inputTokens, outputTokens, estimatedCost);
    }

    private List<LlmUsageDailyBreakdown> BuildDailySeries(IReadOnlyList<TriagemLlmUsageAggregateRow> rows,
        DateOnly from, DateOnly to)
    {
        var byDay = rows.GroupBy(row => row.LocalDate)
            .ToDictionary(group => group.Key, group => new
            {
                Calls = group.Sum(row => row.Calls),
                InputTokens = group.Sum(row => row.InputTokens),
                OutputTokens = group.Sum(row => row.OutputTokens),
                EstimatedCost = EstimateCost(group),
            });

        var result = new List<LlmUsageDailyBreakdown>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            result.Add(byDay.TryGetValue(date, out var agg)
                ? new LlmUsageDailyBreakdown(date, agg.Calls, agg.InputTokens, agg.OutputTokens, agg.EstimatedCost)
                : new LlmUsageDailyBreakdown(date, 0, 0, 0, 0m));
        }

        return result;
    }

    private decimal EstimateCost(IEnumerable<TriagemLlmUsageAggregateRow> rows) =>
        rows.Sum(row => pricingCatalog.EstimateCostUsd(row.ModelId, row.InputTokens, row.OutputTokens));
}
