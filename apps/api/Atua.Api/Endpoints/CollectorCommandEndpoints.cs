using System.Security.Claims;
using Atua.Api.Application.Integrations.CollectorControl;
using Atua.Api.Domain.Integrations.CollectorControl;
using Atua.Api.Infrastructure.Security;

namespace Atua.Api.Endpoints;

/// <summary>
/// Endpoints internos do Agente Coletor (RF-009/ADR-021).
/// Autorizados via credencial de serviço (<c>ServiceCredential</c>).
///
/// Rotas:
/// <list type="bullet">
///   <item><c>POST /api/internal/collector/commands/claim</c></item>
///   <item><c>POST /api/internal/collector/commands/{commandId}/complete</c></item>
/// </list>
///
/// ATENÇÃO: o body da resposta do <c>claim</c> contém credenciais em claro
/// (ADR-021/D9-B). Nenhum middleware deve logar o body desta rota.
/// </summary>
public static class CollectorCommandEndpoints
{
    public static void MapCollectorCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost("/api/internal/collector/commands/claim", ClaimAsync)
            .RequireAuthorization("CollectorCommandClaim");

        endpoints
            .MapPost("/api/internal/collector/commands/{commandId:guid}/complete", CompleteAsync)
            .RequireAuthorization("CollectorCommandComplete");
    }

    // -----------------------------------------------------------------------
    // Claim
    // -----------------------------------------------------------------------

    private static async Task<IResult> ClaimAsync(
        ClaimsPrincipal user,
        ImmediateCollectionCommandService service,
        CancellationToken cancellationToken)
    {
        var integrationId = GetGuidClaim(user, "integration_id");
        if (integrationId is null) return Results.Unauthorized();

        var result = await service.ClaimAsync(integrationId.Value, cancellationToken);

        if (result is null) return Results.NoContent();

        // ADR-021/D9-B: resposta inclui credenciais em claro. Não logar.
        return Results.Ok(new ClaimResponse(
            result.CommandId,
            "ImmediateCollection",
            result.IntegrationId,
            result.TenantId,
            result.ProviderId,
            result.RequestedAtUtc,
            result.ClaimedAtUtc,
            result.ClaimExpiresAtUtc,
            result.HistoryWindowMonths,
            new CredentialPayload(
                result.Credential.Username,
                result.Credential.Password,
                result.Credential.BaseUrl),
            result.PendingDetailWorkOrderIds));
    }

    // -----------------------------------------------------------------------
    // Complete
    // -----------------------------------------------------------------------

    private static async Task<IResult> CompleteAsync(
        Guid commandId,
        ClaimsPrincipal user,
        CompleteRequest body,
        ImmediateCollectionCommandService service,
        CancellationToken cancellationToken)
    {
        var integrationId = GetGuidClaim(user, "integration_id");
        if (integrationId is null) return Results.Unauthorized();

        if (!TryParseOutcome(body.Outcome, out var outcome))
        {
            return Results.BadRequest(new { error = "invalid_outcome" });
        }

        if (outcome == EImmediateCollectionCommandStatus.Failed && body.FailureReason is null)
        {
            return Results.BadRequest(new { error = "failure_reason_required_when_failed" });
        }

        ECommandFailureReason? failureReason = null;
        if (body.FailureReason is not null)
        {
            if (!Enum.TryParse<ECommandFailureReason>(body.FailureReason, ignoreCase: true,
                    out var parsedReason))
            {
                return Results.BadRequest(new { error = "invalid_failure_reason" });
            }

            failureReason = parsedReason;
        }

        var completedAt = body.CompletedAtUtc ?? DateTimeOffset.UtcNow;
        var result = await service.CompleteAsync(
            commandId, integrationId.Value, outcome, failureReason, completedAt, cancellationToken);

        if (result is null) return Results.NotFound(new { error = "command_not_found" });

        return Results.Ok(new CompleteResponse(
            result.CommandId,
            result.Status.ToString(),
            result.CompletedAtUtc));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool TryParseOutcome(string? value, out EImmediateCollectionCommandStatus outcome)
    {
        outcome = default;
        if (value is null) return false;
        if (!Enum.TryParse<EImmediateCollectionCommandStatus>(value, ignoreCase: true, out var parsed))
            return false;
        if (parsed is not (EImmediateCollectionCommandStatus.Succeeded
            or EImmediateCollectionCommandStatus.Failed
            or EImmediateCollectionCommandStatus.Cancelled))
        {
            return false;
        }

        outcome = parsed;
        return true;
    }

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value) ? value : null;

    // -----------------------------------------------------------------------
    // Contratos de entrada/saída
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resposta do claim — inclui credenciais em claro (ADR-021/D9-B).
    /// NUNCA logar este objeto.
    /// </summary>
    private sealed record ClaimResponse(
        Guid CommandId,
        string CommandType,
        Guid IntegrationId,
        Guid TenantId,
        Guid ProviderId,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset ClaimedAtUtc,
        DateTimeOffset ClaimExpiresAtUtc,
        int HistoryWindowMonths,
        CredentialPayload Credential,
        IReadOnlyList<string> PendingDetailWorkOrderIds);

    /// <summary>
    /// Credenciais em claro para o Worker (ADR-021/D9-B).
    /// NUNCA logar.
    /// </summary>
    private sealed record CredentialPayload(string Username, string Password, string? BaseUrl);

    private sealed record CompleteRequest(
        string? Outcome,
        string? FailureReason,
        DateTimeOffset? CompletedAtUtc);

    private sealed record CompleteResponse(
        Guid CommandId,
        string Status,
        DateTimeOffset CompletedAtUtc);
}
