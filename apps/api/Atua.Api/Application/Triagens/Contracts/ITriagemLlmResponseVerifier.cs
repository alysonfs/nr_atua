namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Validação determinística da resposta proposta pela LLM antes do envio ao
/// consumidor (`triagem-llm-conducao.md` §2 e plano §5.3). Obrigatória
/// independentemente do fornecedor — nunca é substituída pela avaliação de
/// um motor de decisão (esse motor é aditivo, conforme o plano, e fora do
/// escopo do M1).
/// </summary>
public interface ITriagemLlmResponseVerifier
{
    TriagemLlmVerificationResult Verify(string respostaAoConsumidor);
}

/// <summary>
/// Resultado da verificação determinística. Quando <see cref="Permitido"/>
/// é <c>false</c>, o chamador nunca envia o texto original — usa uma
/// pergunta de fallback pré-aprovada (<c>TriagemLlmFallbackQuestions</c>)
/// ou encaminha a triagem para intervenção humana.
/// </summary>
public sealed record TriagemLlmVerificationResult(bool Permitido, string? Motivo)
{
    public static readonly TriagemLlmVerificationResult Ok = new(true, Motivo: null);

    public static TriagemLlmVerificationResult Bloqueado(string motivo) => new(false, motivo);
}
