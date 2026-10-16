using Atua.Channel.Contracts;

namespace Atua.Channel.Api;

/// <summary>
/// Cliente HTTP tipado para os endpoints internos do canal (ADR-042):
/// <c>claim</c> e <c>complete</c>. Cada chamada carrega a credencial de
/// serviço (Bearer) correspondente ao escopo exclusivo da operação — nunca
/// o mesmo token para ambas (mesmo princípio de isolamento de escopo do
/// Agente Coletor).
/// </summary>
public interface IChannelApiClient
{
    /// <summary>
    /// Reivindica o próximo comando elegível. Retorna <c>null</c> quando a
    /// API responde 204 (nenhum comando disponível).
    /// </summary>
    Task<ClaimChannelCommandResponse?> ClaimAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Conclui o comando reivindicado com o resultado do envio. Requer o
    /// token de lease (<paramref name="claimedAtUtc"/>) devolvido pelo
    /// claim. Retorna <c>null</c> quando a API responde 409 (lease perdida —
    /// expirada e possivelmente substituída por outro claim; nenhum efeito
    /// foi aplicado pela Master API).
    /// </summary>
    Task<CompleteChannelCommandResponse?> CompleteAsync(
        Guid commandId,
        DateTimeOffset claimedAtUtc,
        string outcome,
        string? errorCode,
        string? errorDescription,
        CancellationToken cancellationToken = default);
}
