using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Perguntas de fallback pré-aprovadas, uma por item do checklist
/// (`triagem-llm-conducao.md` §2, plano §5.3). Usadas quando a resposta
/// proposta pela LLM é bloqueada por <see cref="TriagemLlmResponseVerifier"/>
/// e não há nada seguro para enviar no lugar: o código envia a pergunta de
/// continuação referente ao próximo item pendente do checklist, aplicando a
/// ela as mesmas validações determinísticas (que, por construção, estas
/// perguntas sempre passam).
/// </summary>
public static class TriagemLlmFallbackQuestions
{
    private static readonly IReadOnlyDictionary<ETriagemChecklistItem, string> Questions =
        new Dictionary<ETriagemChecklistItem, string>
        {
            [ETriagemChecklistItem.DescricaoDefeito] =
                "Pode me contar com suas palavras o que está acontecendo com o aparelho?",
            [ETriagemChecklistItem.NotaFiscal] =
                "Pode enviar uma foto legível da nota fiscal do produto, por favor?",
            [ETriagemChecklistItem.Etiqueta] =
                "Pode enviar uma foto legível da etiqueta do aparelho, com modelo e número de série?",
            [ETriagemChecklistItem.MidiaDefeito] =
                "Pode enviar uma foto ou um vídeo curto mostrando o defeito?",
            [ETriagemChecklistItem.Endereco] =
                "Pode me informar o endereço completo onde o produto está instalado (rua, número, bairro e cidade)?",
            [ETriagemChecklistItem.PontoReferencia] =
                "Tem algum ponto de referência do endereço que possa me passar?",
            [ETriagemChecklistItem.Localizacao] =
                "Pode compartilhar a localização do endereço pelo WhatsApp?",
        };

    /// <summary>Pergunta padrão para o item informado — sempre não vazia e dentro do limite de tamanho do verificador.</summary>
    public static string For(ETriagemChecklistItem item) => Questions[item];
}
