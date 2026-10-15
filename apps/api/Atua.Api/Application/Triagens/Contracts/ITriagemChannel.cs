namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Contrato abstrato entre a Master API e o canal de mensageria (arquitetura,
/// seção 2.4 e 5.3; ADR-039). A Master API conhece apenas comandos e
/// resultados estruturados — nenhum detalhe de fornecedor (ex.: Baileys,
/// WhatsApp) atravessa este contrato. Implementações concretas de canal real
/// ficam para incremento futuro; este incremento introduz somente o
/// contrato e um canal fake in-process (ver <see cref="FakeTriagemChannel"/>),
/// suficiente para exercitar o fluxo ponta-a-ponta sem dependência externa.
/// </summary>
public interface ITriagemChannel
{
    /// <summary>
    /// Emite o comando de envio do template inicial (<c>SendTemplate</c>,
    /// arquitetura seção 5.3). Implementações devem ser idempotentes por
    /// <see cref="TriagemSendTemplateCommand.IdempotencyKey"/> (ADR-039,
    /// item 3): reentregas com a mesma chave nunca devem produzir um segundo
    /// envio observável, mesmo sob chamadas concorrentes com a mesma chave.
    /// </summary>
    Task<TriagemChannelSendResult> SendTemplateAsync(TriagemSendTemplateCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// Comando abstrato de envio do template inicial. O identificador e a
/// versão do template são abstratos — nenhum texto literal de fornecedor ou
/// de conteúdo aprovado pelo PO é transportado neste incremento (arquitetura,
/// seção 3: "o texto do template [...] depende de revisão do PO").
/// </summary>
/// <param name="TenantId">Tenant dono da triagem.</param>
/// <param name="IntegrationId">Integração dona da triagem.</param>
/// <param name="TriagemId">Identificador interno da triagem.</param>
/// <param name="WorkOrderId">Identificador interno da OS associada.</param>
/// <param name="TemplateId">Identificador abstrato do template a ser enviado — nunca o texto do template.</param>
/// <param name="TemplateVersion">Versão do template — permite evoluir o texto aprovado pelo PO sem alterar este contrato.</param>
/// <param name="IdempotencyKey">
/// Chave de idempotência estável do comando (ADR-039, item 3; arquitetura,
/// seção 5.1) — a mesma chave em toda retentativa da mesma triagem, nunca
/// gerada novamente a cada tentativa (ADR-039, "Chave nova em cada
/// retentativa" foi rejeitada).
/// </param>
/// <param name="CorrelationId">Identificador de correlação entre comando, resposta do canal e evento de entrega (arquitetura, seção 5.1).</param>
public sealed record TriagemSendTemplateCommand(
    Guid TenantId,
    Guid IntegrationId,
    Guid TriagemId,
    Guid WorkOrderId,
    string TemplateId,
    int TemplateVersion,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Resultado estruturado do envio do comando ao canal — distingue sucesso,
/// falha transitória (elegível a retentativa) e falha definitiva
/// (arquitetura, seção 5.3: "timeout não é interpretado como falha
/// definitiva sem aplicação da política de retentativa").
/// </summary>
/// <param name="Outcome">Resultado tipado — ver <see cref="ETriagemChannelSendOutcome"/>.</param>
/// <param name="ProviderMessageId">Identificador da mensagem no canal, quando aplicável. <c>null</c> fora de <see cref="ETriagemChannelSendOutcome.Success"/>.</param>
/// <param name="ErrorCode">Código seguro de erro do canal — nunca a mensagem de exceção bruta. <c>null</c> em sucesso.</param>
/// <param name="ErrorDescription">Descrição segura do erro, sem dados sensíveis. <c>null</c> em sucesso.</param>
public sealed record TriagemChannelSendResult(
    ETriagemChannelSendOutcome Outcome,
    string? ProviderMessageId,
    string? ErrorCode,
    string? ErrorDescription)
{
    public static TriagemChannelSendResult Success(string? providerMessageId = null) =>
        new(ETriagemChannelSendOutcome.Success, providerMessageId, null, null);

    public static TriagemChannelSendResult TransientFailure(string errorCode, string? errorDescription = null) =>
        new(ETriagemChannelSendOutcome.TransientFailure, null, errorCode, errorDescription);

    public static TriagemChannelSendResult PermanentFailure(string errorCode, string? errorDescription = null) =>
        new(ETriagemChannelSendOutcome.PermanentFailure, null, errorCode, errorDescription);
}
