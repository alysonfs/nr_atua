namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Abstrai a corrida entre a conclusão de um turno de processamento
/// (classificação de visão + conversa da LLM) e um atraso configurável
/// (decisão do PO: avisar o consumidor quando a imagem demora a ser
/// processada, `triagem-llm-conducao.md`). Existe como interface (em vez de
/// <c>Task.WhenAny</c> direto) só para que testes não precisem depender de
/// tempo real — mesmo princípio de <see cref="ITriagemLlmRetryDelay"/>.
/// </summary>
public interface ITriagemProcessingWatchdog
{
    /// <summary>
    /// Retorna <c>true</c> quando <paramref name="delay"/> se esgota antes
    /// de <paramref name="processing"/> concluir (o chamador deve avisar o
    /// consumidor); <c>false</c> quando <paramref name="processing"/> já
    /// concluiu dentro do atraso. Nunca cancela nem aguarda a conclusão de
    /// <paramref name="processing"/> — quem chama continua responsável por
    /// aguardá-la à parte.
    /// </summary>
    Task<bool> HasElapsedBeforeCompletionAsync(Task processing, TimeSpan delay, CancellationToken cancellationToken);
}
