namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Papel de um turno no histórico de conversa minimizado enviado à LLM
/// (`triagem-llm-conducao.md` §2). Não existe papel de operador aqui: a
/// partir de `AssumidaPorOperador` a LLM deixa de ser chamada (regra já
/// definida na arquitetura).
/// </summary>
public enum ETriagemLlmConversationRole
{
    Consumidor,
    Assistente,
}
