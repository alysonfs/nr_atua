using System.Security.Claims;
using Atua.Api.Application.Integrations;

namespace Atua.Api.Endpoints;

/// <summary>
/// Contrato Office do intervalo de coleta recorrente (RF-025). Depende de
/// sessão de browser e membership ativo OWNER/ADMIN no {tenantId} da rota
/// (mesma regra de RF-008/ADR-020).
/// </summary>
public static class RecurrentCollectionIntervalEndpoints
{
    private const string Route =
        "/api/tenants/{tenantId:guid}/integrations/{integrationId:guid}/recurrent-collection-interval";

    public static void MapRecurrentCollectionIntervalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, GetInterval).RequireAuthorization("BrowserSession");
        endpoints.MapPut(Route, SetInterval).RequireAuthorization("BrowserSession");
    }

    private static async Task<IResult> GetInterval(Guid tenantId, Guid integrationId, ClaimsPrincipal user,
        RecurrentCollectionIntervalService service, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var result = await service.GetAsync(userId.Value, tenantId, integrationId, cancellationToken);
        return ToHttpResult(result);
    }

    private static async Task<IResult> SetInterval(Guid tenantId, Guid integrationId, ClaimsPrincipal user,
        SetRecurrentCollectionIntervalRequest request, RecurrentCollectionIntervalService service,
        CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();

        var result = await service.SetAsync(userId.Value, tenantId, integrationId,
            request.RecurrentCollectionIntervalMinutes, cancellationToken);
        return ToHttpResult(result);
    }

    private static IResult ToHttpResult(RecurrentCollectionIntervalResult result) => result.Status switch
    {
        ERecurrentCollectionIntervalStatus.Success => Results.Ok(result.View),
        ERecurrentCollectionIntervalStatus.Forbidden => Results.Forbid(),
        ERecurrentCollectionIntervalStatus.IntegrationNotFound => Results.NotFound(
            new { error = "integration_not_found" }),
        ERecurrentCollectionIntervalStatus.InvalidInterval => Results.BadRequest(
            new { error = "invalid_recurrent_collection_interval" }),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value) ? value : null;
}

public sealed record SetRecurrentCollectionIntervalRequest(int RecurrentCollectionIntervalMinutes);
