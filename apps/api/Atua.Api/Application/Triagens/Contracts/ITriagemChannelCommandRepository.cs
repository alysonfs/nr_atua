using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Contrato de persistência do comando de canal (ADR-042). A criação do
/// comando acontece sempre acoplada ao aceite idempotente da triagem (ver
/// <see cref="ITriagemRepository.GetOrAddWithChannelCommandAsync"/>) — este
/// contrato cobre apenas o ciclo de vida posterior: claim atômico pelo
/// processo do canal e consulta por id/tenant/integração para o
/// <c>complete</c>.
/// </summary>
public interface ITriagemChannelCommandRepository
{
    /// <summary>
    /// Reivindica atomicamente o próximo comando elegível (<c>Pending</c>,
    /// ou <c>Claimed</c> com lease expirada) da tenant/integração
    /// informada. Retorna <c>null</c> quando não há comando elegível.
    /// </summary>
    Task<TriagemChannelCommand?> ClaimNextAsync(Guid tenantId, Guid integrationId, TimeSpan leaseDuration,
        DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Consulta o comando por id, restrito à tenant/integração informada
    /// (isolamento multi-tenant — nunca encontra um comando de outra
    /// tenant/integração). Retorna <c>null</c> quando não encontrado.
    /// </summary>
    Task<TriagemChannelCommand?> FindByIdAsync(Guid commandId, Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persiste transições já aplicadas em memória (ex.:
    /// <c>MarkCompletedSuccess</c>, <c>RecordTransientFailure</c>) — e
    /// quaisquer outras mutações pendentes no mesmo <c>DbContext</c> (ex.:
    /// a transição correspondente de <see cref="Triagem"/>), na mesma
    /// transação.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
