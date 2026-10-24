using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Fake thread-safe de <see cref="ITriagemChannelCommandRepository"/>
/// (ADR-042) para testar <c>TriagemChannelCommandService</c> sem PostgreSQL
/// real. Diferente do repositório real (SQL bruto com <c>FOR UPDATE SKIP
/// LOCKED</c>), este fake simula o mesmo efeito observável — nenhum comando
/// é entregue duas vezes simultaneamente — usando um lock em memória, o que
/// é suficiente para single-process, mas não prova concorrência real entre
/// processos (ver limitação documentada em
/// <see cref="TriagemChannelCommandClaimSqlTests"/>).
/// </summary>
internal sealed class FakeTriagemChannelCommandRepository : ITriagemChannelCommandRepository
{
    private readonly List<TriagemChannelCommand> _commands = new();
    private readonly object _lock = new();

    public void Add(TriagemChannelCommand command)
    {
        lock (_lock)
        {
            _commands.Add(command);
        }
    }

    public Task<TriagemChannelCommand?> ClaimNextAsync(Guid tenantId, Guid integrationId, TimeSpan leaseDuration,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var next = _commands
                .Where(command => command.TenantId == tenantId && command.IntegrationId == integrationId)
                .Where(command => command.Status == ETriagemChannelCommandStatus.Pending
                    || (command.Status == ETriagemChannelCommandStatus.Claimed
                        && command.ClaimExpiresAtUtc is not null && command.ClaimExpiresAtUtc < now))
                .OrderBy(command => command.CreatedAtUtc)
                .FirstOrDefault();

            if (next is null)
            {
                return Task.FromResult<TriagemChannelCommand?>(null);
            }

            next.Claim(now, now.Add(leaseDuration));
            return Task.FromResult<TriagemChannelCommand?>(next);
        }
    }

    public Task<IReadOnlyList<TriagemChannelCommand>> ListCompletedSinceAsync(Guid tenantId, Guid integrationId,
        DateTimeOffset completedSinceUtc, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            // Replica a ordenação por recência + limite (256) do repositório real
            // (TriagemChannelCommandRepository.MaxRetransmissionCandidates).
            IReadOnlyList<TriagemChannelCommand> result = _commands
                .Where(command => command.TenantId == tenantId && command.IntegrationId == integrationId
                    && command.Status == ETriagemChannelCommandStatus.Completed
                    && command.CompletedAtUtc >= completedSinceUtc)
                .OrderByDescending(command => command.CompletedAtUtc)
                .Take(TriagemChannelCommandRepository.MaxRetransmissionCandidates)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<TriagemChannelCommand?> FindByIdAsync(Guid commandId, Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var command = _commands.SingleOrDefault(candidate => candidate.Id == commandId
                && candidate.TenantId == tenantId && candidate.IntegrationId == integrationId);
            return Task.FromResult(command);
        }
    }

    public Task<bool> ExistsByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            return Task.FromResult(_commands.Any(command => command.IdempotencyKey == idempotencyKey));
        }
    }

    public IReadOnlyList<TriagemChannelCommand> Commands
    {
        get
        {
            lock (_lock)
            {
                return _commands.ToList();
            }
        }
    }

    public int SaveChangesCallCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            SaveChangesCallCount++;
        }

        return Task.CompletedTask;
    }
}
