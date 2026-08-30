using System.Security.Claims;
using Atua.Api.Application.Integrations.CollectorControl;

namespace Atua.Api.Endpoints;

/// <summary>
/// Contrato Office do controle de ativação do Agente Coletor
/// (RF-008/ADR-020). Depende de sessão de browser e membership ativo
/// <c>OWNER</c>/<c>ADMIN</c> no <c>{tenantId}</c> da rota.
///
/// Escopo deste portão: apenas estado e criação do comando <c>Pending</c>.
/// Os endpoints internos de claim/complete do Worker não fazem parte daqui.
/// </summary>
public static class CollectorActivationEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private const string Route =
        "/api/tenants/{tenantId:guid}/integrations/{integrationId:guid}/collector-activation";

    public static void MapCollectorActivationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, GetActivation).RequireAuthorization("BrowserSession");
        endpoints.MapPut(Route, Activate).RequireAuthorization("BrowserSession");
        endpoints.MapDelete(Route, Deactivate).RequireAuthorization("BrowserSession");
    }

    private static async Task<IResult> GetActivation(Guid tenantId, Guid integrationId,
        ClaimsPrincipal user, CollectorActivationService service, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var view = await service.GetAsync(userId.Value, tenantId, integrationId, cancellationToken);
        // Recursos fora do tenant ou sem membership não são revelados
        // indevidamente (RF-008.2/ADR-020).
        return view is null ? Results.Forbid() : Results.Ok(view);
    }

    private static async Task<IResult> Activate(Guid tenantId, Guid integrationId, ClaimsPrincipal user,
        HttpRequest request, CollectorActivationService service, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var result = await service.ActivateAsync(userId.Value, tenantId, integrationId,
            GetIdempotencyKey(request), cancellationToken);
        return ToHttpResult(result);
    }

    private static async Task<IResult> Deactivate(Guid tenantId, Guid integrationId, ClaimsPrincipal user,
        HttpRequest request, CollectorActivationService service, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var result = await service.DeactivateAsync(userId.Value, tenantId, integrationId,
            GetIdempotencyKey(request), cancellationToken);
        return ToHttpResult(result);
    }

    private static IResult ToHttpResult(CollectorActivationResult result) => result.Status switch
    {
        ECollectorActivationStatusResult.Success => Results.Ok(result.View),
        ECollectorActivationStatusResult.Forbidden => Results.Forbid(),
        ECollectorActivationStatusResult.IntegrationNotFound => Results.NotFound(
            new { error = "integration_not_found" }),
        ECollectorActivationStatusResult.NotEligible => Results.Conflict(new
        {
            error = "activation_not_eligible",
            activationBlockReason = result.BlockReason.ToString()
        }),
        ECollectorActivationStatusResult.MissingIdempotencyKey => Results.BadRequest(
            new { error = "missing_idempotency_key" }),
        ECollectorActivationStatusResult.IdempotencyKeyConflict => Results.Conflict(
            new { error = "idempotency_key_conflict" }),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    private static string? GetIdempotencyKey(HttpRequest request) =>
        request.Headers.TryGetValue(IdempotencyKeyHeader, out var values)
            ? values.ToString()
            : null;

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value) ? value : null;
}
