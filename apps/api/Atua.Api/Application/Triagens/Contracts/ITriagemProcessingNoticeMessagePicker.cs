namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Escolhe o texto do aviso de "imagem em análise" enviado ao consumidor
/// (decisão do PO: variar de forma aleatória entre opções curtas e
/// cordiais, sem prometer resultado — `triagem-llm-conducao.md`). Existe
/// como interface (em vez de <c>Random</c> direto) para que testes do
/// <see cref="TriagemProcessingNoticeSender"/> sejam determinísticos.
/// </summary>
public interface ITriagemProcessingNoticeMessagePicker
{
    /// <summary>Uma das mensagens aprovadas, escolhida aleatoriamente a cada chamada.</summary>
    string Pick();
}
