namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Resultado de uma chamada ao fornecedor de LLM da triagem (ADR-048,
/// `triagem-llm-conducao.md` §2/§5). Usado tanto pelo contrato
/// <c>ITriagemLlmClient</c> quanto pelo registro de uso por tenant
/// (<see cref="TriagemLlmUsage"/>) — a mesma chamada que falha ainda é
/// registrada, com o motivo da falha.
/// </summary>
public enum ETriagemLlmOutcome
{
    /// <summary>Saída validada contra o esquema (`respostaAoConsumidor`, `camposEstruturados`, `itensAtendidos`, `sinalDesvio`).</summary>
    Success,

    /// <summary>
    /// O fornecedor respondeu, mas a saída não seguiu o esquema esperado
    /// (campo ausente, tipo inválido, confiança fora de [0,1] etc.) — conta
    /// como tentativa inválida (`triagem-llm-conducao.md` §2). O texto nunca
    /// é enviado ao consumidor.
    /// </summary>
    InvalidOutput,

    /// <summary>
    /// O fornecedor não respondeu dentro do tempo limite, recusou a chamada
    /// ou falhou de forma transitória/permanente — o chamador aplica a
    /// política de retentativa e fallback já definida
    /// (`triagem-llm-conducao.md` §4).
    /// </summary>
    ProviderUnavailable,
}
