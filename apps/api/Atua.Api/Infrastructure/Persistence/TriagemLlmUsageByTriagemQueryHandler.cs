using Atua.Api.Application.LlmUsage.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Infrastructure.Persistence;

/// <summary>
/// Implementação EF Core de <see cref="ITriagemLlmUsageByTriagemQuery"/>
/// (ADR-048). O <c>GroupBy</c> é traduzido para <c>GROUP BY</c> no banco —
/// só os grupos (triagem × modelo) chegam à aplicação, nunca as linhas
/// brutas de <c>TriagemLlmUsage</c>.
/// </summary>
public sealed class TriagemLlmUsageByTriagemQueryHandler(AtuaDbContext dbContext) : ITriagemLlmUsageByTriagemQuery
{
    public async Task<IReadOnlyList<TriagemLlmUsageByTriagemRow>> ExecuteAsync(
        IReadOnlyCollection<Guid> triagemIds, CancellationToken cancellationToken)
    {
        if (triagemIds.Count == 0) return [];

        var grouped = await dbContext.TriagemLlmUsages.AsNoTracking()
            .Where(usage => triagemIds.Contains(usage.TriagemId))
            .GroupBy(usage => new { usage.TriagemId, usage.ModelId })
            .Select(group => new
            {
                group.Key.TriagemId,
                group.Key.ModelId,
                Calls = group.Count(),
                InputTokens = group.Sum(usage => (long)usage.InputTokens),
                OutputTokens = group.Sum(usage => (long)usage.OutputTokens),
            })
            .ToListAsync(cancellationToken);

        return grouped.Select(row => new TriagemLlmUsageByTriagemRow(
                row.TriagemId, row.ModelId, row.Calls, row.InputTokens, row.OutputTokens))
            .ToList();
    }
}
