using Atua.Api.Application.LlmUsage.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Infrastructure.Persistence;

/// <summary>
/// Implementação EF Core de <see cref="ITriagemLlmUsageAggregateQuery"/>
/// (ADR-048). O <c>GroupBy</c> é traduzido para <c>GROUP BY</c> no banco —
/// só os grupos (tenant × modelo × dia local) chegam à aplicação, nunca as
/// linhas brutas de <see cref="TriagemLlmUsage"/>.
/// </summary>
public sealed class TriagemLlmUsageAggregateQueryHandler(AtuaDbContext dbContext) : ITriagemLlmUsageAggregateQuery
{
    // Mesma simplificação documentada em GetLlmUsageSummaryUseCase: Brasil
    // não observa horário de verão desde 2019, então America/Sao_Paulo é
    // sempre UTC-3 para os dados existentes (tabela criada em 2026-10-23).
    private static readonly TimeSpan SaoPauloOffset = TimeSpan.FromHours(-3);

    public async Task<IReadOnlyList<TriagemLlmUsageAggregateRow>> ExecuteAsync(IReadOnlyCollection<Guid> tenantIds,
        DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcExclusive, CancellationToken cancellationToken)
    {
        if (tenantIds.Count == 0) return [];

        var grouped = await dbContext.TriagemLlmUsages.AsNoTracking()
            .Where(usage => tenantIds.Contains(usage.TenantId)
                && usage.CreatedAtUtc >= fromUtcInclusive && usage.CreatedAtUtc < toUtcExclusive)
            .GroupBy(usage => new
            {
                usage.TenantId,
                usage.ModelId,
                LocalDate = (usage.CreatedAtUtc + SaoPauloOffset).Date,
            })
            .Select(group => new
            {
                group.Key.TenantId,
                group.Key.ModelId,
                group.Key.LocalDate,
                Calls = group.Count(),
                InputTokens = group.Sum(usage => (long)usage.InputTokens),
                OutputTokens = group.Sum(usage => (long)usage.OutputTokens),
                TotalLatencyMs = group.Sum(usage => usage.LatencyMs),
                FailedCalls = group.Count(usage => usage.Outcome != ETriagemLlmOutcome.Success),
            })
            .ToListAsync(cancellationToken);

        return grouped.Select(row => new TriagemLlmUsageAggregateRow(
                row.TenantId, row.ModelId, DateOnly.FromDateTime(row.LocalDate), row.Calls, row.InputTokens,
                row.OutputTokens, row.TotalLatencyMs, row.FailedCalls))
            .ToList();
    }
}
