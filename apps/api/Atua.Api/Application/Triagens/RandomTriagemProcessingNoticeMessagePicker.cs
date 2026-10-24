using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Implementação de produção de <see cref="ITriagemProcessingNoticeMessagePicker"/>:
/// mensagens curtas e cordiais em pt-BR, aprovadas pelo
/// <see cref="ITriagemLlmResponseVerifier"/>, que nunca prometem resultado
/// (decisão do PO — `triagem-llm-conducao.md`). Escolhidas aleatoriamente
/// via <see cref="Random.Shared"/> a cada chamada.
/// </summary>
public sealed class RandomTriagemProcessingNoticeMessagePicker : ITriagemProcessingNoticeMessagePicker
{
    /// <summary>Opções aprovadas — nunca mencionam prazo, agendamento, preço/orçamento, garantia, links ou dados sensíveis.</summary>
    internal static readonly string[] Messages =
    [
        "Recebi sua imagem, estou analisando 🔎",
        "Só um instante, estou verificando a imagem que você enviou.",
        "Imagem recebida! Estou processando, já te respondo.",
    ];

    public string Pick() => Messages[Random.Shared.Next(Messages.Length)];
}
