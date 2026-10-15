namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Resultado tipado da emissão de um comando de envio ao canal (ADR-039;
/// arquitetura, seção 5.3). Distingue sucesso, falha transitória (elegível a
/// retentativa, mesma chave de idempotência) e falha definitiva (convertida
/// em fato de decisão para a Master API — ver <c>Triagem.MarcarFalhaDeContato</c>).
/// </summary>
public enum ETriagemChannelSendOutcome
{
    /// <summary>O canal aceitou e confirmou o envio do comando.</summary>
    Success,

    /// <summary>
    /// Falha momentânea do canal/transporte (ex.: timeout, indisponibilidade
    /// pontual). Elegível a retentativa preservando a mesma chave de
    /// idempotência (ADR-039, item 7).
    /// </summary>
    TransientFailure,

    /// <summary>
    /// Falha definitiva reportada pelo canal (ex.: número inválido,
    /// destinatário bloqueado). Não é elegível a retentativa — a Master API
    /// decide a transição de estado correspondente.
    /// </summary>
    PermanentFailure,
}
