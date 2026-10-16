namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Discrimina o resultado de <c>TriagemChannelCommandService.CompleteAsync</c>
/// (ADR-042, correção de concorrência do token de lease). Distingue a
/// aplicação do efeito (inclusive replay idempotente de um comando já
/// terminal) da perda de posse da lease — quando o chamador não é mais o
/// titular do comando reivindicado.
/// </summary>
public enum ETriagemChannelCompleteOutcome
{
    /// <summary>
    /// O efeito foi aplicado agora, ou o comando já estava em estado
    /// terminal (<c>Completed</c>/<c>Failed</c>) e o resultado conhecido foi
    /// devolvido sem reaplicar nenhum efeito (replay idempotente).
    /// </summary>
    Applied,

    /// <summary>
    /// O comando não está mais sob a lease do chamador: ou a lease expirou e
    /// o comando voltou a <c>Pending</c>, ou — cenário corrigido por esta
    /// mudança — a lease expirou e foi substituída por outro claim (o token
    /// <c>ClaimedAtUtc</c> apresentado não é mais o vigente). Nenhum efeito é
    /// aplicado ao comando ou à triagem.
    /// </summary>
    LeaseLost,
}
