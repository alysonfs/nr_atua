using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Implementação concreta de <see cref="ITriagemOutboxRepository"/> sobre o
/// PostgreSQL da Master API, via <see cref="AtuaDbContext"/> (ADR-041).
///
/// O claim em lote usa SQL bruto (<see cref="TriagemOutboxPersistenceSchema.ClaimBatchSql"/>)
/// porque LINQ/EF Core não expressa <c>FOR UPDATE SKIP LOCKED</c>; o restante
/// do ciclo de vida (marcar processada/ignorada/com falha) usa o change
/// tracker normal do EF sobre as entidades já retornadas pelo claim.
/// </summary>
public sealed class TriagemOutboxRepository(AtuaDbContext dbContext) : ITriagemOutboxRepository
{
    public async Task<IReadOnlyList<TriagemTriggerOutbox>> ClaimBatchAsync(int batchSize, TimeSpan leaseDuration,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var claimExpiresAtUtc = now.Add(leaseDuration);

        return await dbContext.Set<TriagemTriggerOutbox>()
            .FromSqlRaw(
                TriagemOutboxPersistenceSchema.ClaimBatchSql,
                new NpgsqlParameter("@now", now),
                new NpgsqlParameter("@claimExpiresAtUtc", claimExpiresAtUtc),
                new NpgsqlParameter("@batchSize", batchSize),
                new NpgsqlParameter("@allowedWorkOrderIds", TriagemTestSafeguard.AllowedWorkOrderIds),
                new NpgsqlParameter("@syntheticWorkOrderProviderIdPrefix",
                    TriagemTestSafeguard.SyntheticWorkOrderProviderIdPrefix))
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
