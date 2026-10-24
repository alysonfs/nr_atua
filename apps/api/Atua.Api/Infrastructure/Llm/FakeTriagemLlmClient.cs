using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>
/// Implementação determinística de <see cref="ITriagemLlmClient"/> para uso
/// em desenvolvimento (quando <c>TriagemLlm:Enabled</c> é <c>false</c>) e em
/// testes — nenhuma chamada de rede é feita. O comportamento padrão é
/// simples e previsível: marca como atendido todo item pendente para o qual
/// encontra evidência textual mínima no último turno do consumidor, e pede
/// o próximo item pendente na resposta.
///
/// Testes que precisam de um resultado específico devem usar
/// <see cref="EnqueueResult"/> para programar a próxima resposta, em vez de
/// depender da heurística padrão.
/// </summary>
public sealed class FakeTriagemLlmClient : ITriagemLlmClient
{
    private readonly Queue<TriagemLlmResult> queuedResults = new();

    /// <summary>
    /// Atraso artificial opcional antes de devolver o resultado (produção:
    /// sempre <see cref="TimeSpan.Zero"/>; testes: usado para simular um
    /// turno "demorado" de forma determinística, já que a chamada real ao
    /// Bedrock é I/O — ver <c>TriagemLlmConversationServiceTests</c>, aviso
    /// de "imagem em análise").
    /// </summary>
    public TimeSpan ResponseDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Programa o próximo resultado a ser devolvido por <see cref="ConduzirAsync"/>, em ordem FIFO.</summary>
    public void EnqueueResult(TriagemLlmResult result) => queuedResults.Enqueue(result);

    public async Task<TriagemLlmResult> ConduzirAsync(TriagemLlmRequest request, CancellationToken cancellationToken)
    {
        if (ResponseDelay > TimeSpan.Zero)
        {
            await Task.Delay(ResponseDelay, cancellationToken);
        }

        if (queuedResults.Count > 0)
        {
            return queuedResults.Dequeue();
        }

        var pendente = request.ChecklistState.FirstOrDefault(item => !item.Atendido);
        var resposta = pendente is null
            ? "Obrigado! Recebemos todas as informações necessárias para seguir com a sua triagem."
            : TriagemLlmFallbackQuestions.For(pendente.Item);

        var campos = new TriagemLlmCamposEstruturados(
            TipoDefeito: null, Sintoma: null, CodigoErro: null, Categoria: null);
        var payload = new TriagemLlmSuccessPayload(
            resposta,
            campos,
            ItensAtendidos: [],
            SinalDesvio: new TriagemLlmSinalDesvio(Detectado: false, Motivo: null));
        var usage = new TriagemLlmUsageInfo(ModelId: "fake-model", InputTokens: 0, OutputTokens: 0, Latencia: TimeSpan.Zero);

        return TriagemLlmResult.Success(payload, usage);
    }
}
