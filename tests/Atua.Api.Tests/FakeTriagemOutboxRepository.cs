using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Fake de <see cref="ITriagemOutboxRepository"/> para testes de
/// <c>TriagemOutboxDrainService</c> (ADR-041). Simula em memória a semântica
/// de claim por lote que, em produção, só o PostgreSQL (<c>FOR UPDATE SKIP
/// LOCKED</c> + lease) consegue garantir atomicamente — ver limitação
/// documentada em <see cref="TriagemOutboxClaimSqlTests"/>. Como as entidades
/// retornadas pelo claim já são as mesmas instâncias guardadas aqui,
/// <see cref="SaveChangesAsync"/> é um no-op (sem "change tracker" a
/// persistir).
/// </summary>
internal sealed class FakeTriagemOutboxRepository : ITriagemOutboxRepository
{
    private readonly List<TriagemTriggerOutbox> _items = [];

    public int SaveChangesCallCount { get; private set; }

    public void Add(TriagemTriggerOutbox item) => _items.Add(item);

    public IReadOnlyList<TriagemTriggerOutbox> Items => _items;

    public Task<IReadOnlyList<TriagemTriggerOutbox>> ClaimBatchAsync(int batchSize, TimeSpan leaseDuration,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Não verifica cancellationToken aqui propositalmente: simula uma
        // consulta já em andamento no PostgreSQL real, que não é abortada a
        // meio de uma transação de claim já iniciada — o cancelamento só é
        // observado pelo DrainService entre o claim e o processamento de
        // cada item do lote (ver TriagemOutboxDrainServiceTests).
        var claimExpiresAtUtc = now.Add(leaseDuration);

        var claimable = _items
            .Where(item =>
                (item.Status == ETriagemTriggerOutboxStatus.Pending && item.NextAttemptAtUtc <= now) ||
                (item.Status == ETriagemTriggerOutboxStatus.Processing && item.ClaimExpiresAtUtc < now))
            .OrderBy(item => item.NextAttemptAtUtc)
            .Take(batchSize)
            .ToList();

        foreach (var item in claimable)
        {
            item.Claim(now, claimExpiresAtUtc);
        }

        return Task.FromResult<IReadOnlyList<TriagemTriggerOutbox>>(claimable);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}
