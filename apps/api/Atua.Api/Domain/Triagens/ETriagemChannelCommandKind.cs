namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Distingue o tipo de conteúdo de um <see cref="TriagemChannelCommand"/>
/// (plano M2, docs/implementation/PLANO-triagem-llm-jev.md §3, item 8): o
/// template inicial fixo (ADR-041/ADR-042) e a resposta conduzida pela LLM
/// (`triagem-llm-conducao.md`). Ambos reaproveitam a mesma fila/outbox de
/// saída — apenas o efeito aplicado pelo <c>complete</c> em
/// <see cref="Application.Triagens.TriagemChannelCommandService"/> difere:
/// somente <see cref="TemplateInicial"/> aplica a transição
/// <c>MarcarContatoIniciado</c>/<c>MarcarFalhaDeContato</c> — a resposta da
/// LLM nunca reaplica nem reinterpreta essa transição, porque quem decide o
/// estado da triagem durante a conversa é o domínio <see cref="Triagem"/>,
/// já aplicado antes do comando ser persistido.
/// </summary>
public enum ETriagemChannelCommandKind
{
    TemplateInicial,
    RespostaLlm,
}
