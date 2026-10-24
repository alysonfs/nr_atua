using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Tests;

/// <summary>
/// Fake de <see cref="ITriagemProcessingNoticeSender"/> — apenas registra as
/// requisições recebidas, para que os testes de
/// <c>TriagemLlmConversationServiceTests</c> verifiquem quando (e com quais
/// dados) o aviso de "imagem em análise" é enviado, sem precisar de um
/// escopo de DI real.
/// </summary>
internal sealed class FakeTriagemProcessingNoticeSender : ITriagemProcessingNoticeSender
{
    public List<TriagemProcessingNoticeRequest> Requests { get; } = [];

    /// <summary>Executado dentro de <see cref="SendAsync"/> — usado para observar o estado de outras dependências no exato momento do envio (ex.: ordem em relação à resposta da LLM).</summary>
    public Action? OnSend { get; set; }

    public Task SendAsync(TriagemProcessingNoticeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OnSend?.Invoke();
        Requests.Add(request);
        return Task.CompletedTask;
    }
}
