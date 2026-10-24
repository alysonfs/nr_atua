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
    /// Agenda um novo comando de canal (plano M2) fora do fluxo de aceite
    /// idempotente de <see cref="ITriagemRepository.GetOrAddWithChannelCommandAsync"/>
    /// — usado pela condução da conversa pela LLM
    /// (<see cref="ETriagemChannelCommandKind.RespostaLlm"/>) para enfileirar
    /// a resposta aprovada/fallback no mesmo <c>DbContext</c> da transição de
    /// <see cref="Triagem"/> já aplicada em memória. Requer
    /// <see cref="SaveChangesAsync"/> subsequente para persistir.
    /// </summary>
    void Add(TriagemChannelCommand command);

    /// <summary>
    /// Lista comandos concluídos com sucesso da tenant/integração desde
    /// <paramref name="completedSinceUtc"/> — usada somente para atender
    /// pedidos de retransmissão do WhatsApp após reinício do canal.
    /// </summary>
    Task<IReadOnlyList<TriagemChannelCommand>> ListCompletedSinceAsync(Guid tenantId, Guid integrationId,
        DateTimeOffset completedSinceUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Verifica se já existe um comando com a chave de idempotência
    /// informada (plano M2 — idempotência por mensagem inbound). Usado
    /// antes de chamar a LLM, para não repetir a chamada quando o mesmo
    /// evento inbound é reentregue.
    /// </summary>
    Task<bool> ExistsByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Persiste transições já aplicadas em memória (ex.:
    /// <c>MarkCompletedSuccess</c>, <c>RecordTransientFailure</c>) — e
    /// quaisquer outras mutações pendentes no mesmo <c>DbContext</c> (ex.:
    /// a transição correspondente de <see cref="Triagem"/>), na mesma
    /// transação.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
