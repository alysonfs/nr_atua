using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Infrastructure.Persistence;

/// <summary>
/// Implementação EF Core de <see cref="ITriagemLlmUsageRepository"/>
/// (ADR-048). Apenas inserção — este registro nunca é atualizado, só lido
/// para relatório de consumo (pendência futura do ADR-048).
/// </summary>
public sealed class TriagemLlmUsageRepository(AtuaDbContext dbContext) : ITriagemLlmUsageRepository
{
    public async Task RecordAsync(TriagemLlmUsageRecord record, CancellationToken cancellationToken)
    {
        var usage = new TriagemLlmUsage(
            Guid.CreateVersion7(),
            record.TenantId,
            record.TriagemId,
            record.ModelId,
            record.InputTokens,
            record.OutputTokens,
            record.LatencyMs,
            record.Outcome,
            record.CreatedAtUtc);

        dbContext.TriagemLlmUsages.Add(usage);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountCallsAsync(Guid triagemId, CancellationToken cancellationToken) =>
        dbContext.TriagemLlmUsages.CountAsync(usage => usage.TriagemId == triagemId, cancellationToken);
}
