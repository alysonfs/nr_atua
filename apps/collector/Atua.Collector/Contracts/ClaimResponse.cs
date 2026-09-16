namespace Atua.Collector.Contracts;

/// <summary>
/// Resposta do endpoint <c>POST /api/internal/collector/commands/claim</c>.
/// ATENÇÃO — ADR-021/D9-B: este objeto contém credenciais em claro.
/// NUNCA logar este objeto nem seus campos sensíveis (Password, BaseUrl).
/// </summary>
public sealed record ClaimResponse(
    Guid CommandId,
    string CommandType,
    Guid IntegrationId,
    Guid TenantId,
    Guid ProviderId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset ClaimExpiresAtUtc,
    int HistoryWindowMonths,
    CredentialPayload Credential);
