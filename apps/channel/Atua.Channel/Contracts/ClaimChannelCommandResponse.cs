namespace Atua.Channel.Contracts;

/// <summary>
/// Espelha <c>ClaimChannelCommandResponse</c> da Master API (ADR-042,
/// <c>POST /api/internal/triagens/canal/claim</c>). Apenas os campos
/// necessários para o processo do canal enviar o template e reportar o
/// resultado, incluindo o snapshot renderizado selecionado pela Master API.
/// </summary>
public sealed record ClaimChannelCommandResponse(
    Guid CommandId,
    Guid TriagemId,
    Guid WorkOrderId,
    string TemplateId,
    int TemplateVersion,
    string IdempotencyKey,
    Guid CorrelationId,
    int AttemptCount,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset ClaimExpiresAtUtc,
    string RenderedContent = "");
