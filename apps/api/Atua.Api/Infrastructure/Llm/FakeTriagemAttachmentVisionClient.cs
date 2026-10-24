using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>
/// Implementação determinística de <see cref="ITriagemAttachmentVisionClient"/>
/// para uso em desenvolvimento (quando <c>TriagemLlm:Enabled</c> é
/// <c>false</c>) e em testes — nenhuma chamada de rede é feita. Mesmo
/// padrão de <see cref="FakeTriagemLlmClient"/>: por padrão nunca classifica
/// nada (retorna <c>Invalid</c>, item fica pendente) — testes que precisam
/// de um resultado específico devem usar <see cref="EnqueueResult"/>.
/// </summary>
public sealed class FakeTriagemAttachmentVisionClient : ITriagemAttachmentVisionClient
{
    private readonly Queue<TriagemAttachmentVisionResult> queuedResults = new();

    /// <summary>Programa o próximo resultado a ser devolvido por <see cref="ClassificarAsync"/>, em ordem FIFO.</summary>
    public void EnqueueResult(TriagemAttachmentVisionResult result) => queuedResults.Enqueue(result);

    /// <summary>Requisições efetivamente recebidas nos testes, sem chamadas de rede.</summary>
    public List<TriagemAttachmentVisionRequest> Requests { get; } = [];

    public Task<TriagemAttachmentVisionResult> ClassificarAsync(
        TriagemAttachmentVisionRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (queuedResults.Count > 0)
        {
            return Task.FromResult(queuedResults.Dequeue());
        }

        return Task.FromResult(TriagemAttachmentVisionResult.Invalid("nenhum resultado programado no fake"));
    }
}
