using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Implementação concreta de <see cref="ITriagemChannelCommandRepository"/>
/// sobre o PostgreSQL da Master API, via <see cref="AtuaDbContext"/>
/// (ADR-042).
///
/// O claim usa SQL bruto (<see cref="TriagemChannelCommandPersistenceSchema.ClaimNextSql"/>)
/// pelo mesmo motivo de <c>TriagemOutboxRepository</c>: LINQ/EF Core não
/// expressa <c>FOR UPDATE SKIP LOCKED</c>.
/// </summary>
public sealed class TriagemChannelCommandRepository(AtuaDbContext dbContext) : ITriagemChannelCommandRepository
{
    public async Task<TriagemChannelCommand?> ClaimNextAsync(Guid tenantId, Guid integrationId,
        TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var claimExpiresAtUtc = now.Add(leaseDuration);

        var claimed = await dbContext.Set<TriagemChannelCommand>()
            .FromSqlRaw(
                TriagemChannelCommandPersistenceSchema.ClaimNextSql,
                new NpgsqlParameter("@tenantId", tenantId),
                new NpgsqlParameter("@integrationId", integrationId),
                new NpgsqlParameter("@now", now),
                new NpgsqlParameter("@claimExpiresAtUtc", claimExpiresAtUtc))
            .ToListAsync(cancellationToken);

        return claimed.SingleOrDefault();
    }

    public Task<TriagemChannelCommand?> FindByIdAsync(Guid commandId, Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken) =>
        dbContext.Set<TriagemChannelCommand>().FirstOrDefaultAsync(
            command => command.Id == commandId && command.TenantId == tenantId
                && command.IntegrationId == integrationId,
            cancellationToken);

    public void Add(TriagemChannelCommand command) => dbContext.Set<TriagemChannelCommand>().Add(command);

    /// <summary>
    /// Limite de candidatos por chamada — mesma ordem de grandeza da
    /// capacidade do cache em memória do canal (<c>MAX_OUTBOUND_RETRY_MESSAGES</c>,
    /// 256), para delimitar o custo da varredura por hash sem depender de um
    /// índice dedicado (opção A, sem migration).
    /// </summary>
    public const int MaxRetransmissionCandidates = 256;

    public async Task<IReadOnlyList<TriagemChannelCommand>> ListCompletedSinceAsync(Guid tenantId,
        Guid integrationId, DateTimeOffset completedSinceUtc, CancellationToken cancellationToken) =>
        await dbContext.Set<TriagemChannelCommand>()
            .AsNoTracking()
            .Where(command => command.TenantId == tenantId && command.IntegrationId == integrationId
                && command.Status == ETriagemChannelCommandStatus.Completed
                && command.CompletedAtUtc >= completedSinceUtc)
            .OrderByDescending(command => command.CompletedAtUtc)
            .Take(MaxRetransmissionCandidates)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.Set<TriagemChannelCommand>()
            .AnyAsync(command => command.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
