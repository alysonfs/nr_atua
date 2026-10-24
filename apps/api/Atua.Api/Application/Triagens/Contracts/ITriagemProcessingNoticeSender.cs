namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Persiste o aviso de "imagem em análise" ao consumidor quando um turno
/// com anexo pendente de classificação não conclui dentro do atraso
/// configurado (<see cref="TriagemLlmConversationOptions.ProcessingNoticeDelayMs"/>,
/// decisão do PO — `triagem-llm-conducao.md`). A implementação de produção
/// usa um escopo de DI próprio (nunca o mesmo <c>AtuaDbContext</c> scoped do
/// turno em andamento em <see cref="TriagemLlmConversationService"/>, que
/// continua em execução concorrentemente) — ver
/// <c>TriagemProcessingNoticeSender</c>.
/// </summary>
public interface ITriagemProcessingNoticeSender
{
    /// <summary>
    /// Idempotente por <see cref="TriagemProcessingNoticeRequest.InboundMessageId"/>
    /// — reentregas do mesmo evento inbound nunca duplicam o aviso. Nunca
    /// chama a LLM nem conta no orçamento de chamadas da triagem.
    /// </summary>
    Task SendAsync(TriagemProcessingNoticeRequest request, CancellationToken cancellationToken);
}

/// <summary>Dados mínimos necessários para montar o <c>TriagemChannelCommand</c> de aviso.</summary>
public sealed record TriagemProcessingNoticeRequest(
    Guid TenantId,
    Guid IntegrationId,
    Guid TriagemId,
    Guid WorkOrderId,
    Guid InboundMessageId,
    string ConsumerPhone);
