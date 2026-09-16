using System.Security.Claims;
using Atua.Api.Application.Identity;

namespace Atua.Api.Endpoints;

/// <summary>
/// Preferências de fuso horário (RF-005/ADR-015): override por sessão de
/// browser e definição do fuso horário padrão do tenant.
/// </summary>
public static class TimeZoneEndpoints
{
    public static void MapTimeZoneEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/sessions/current/timezone", SetSessionTimeZone)
            .RequireAuthorization("BrowserSession");
        endpoints.MapPut("/api/tenants/{tenantId:guid}/timezone", SetTenantTimeZone)
            .RequireAuthorization("BrowserSession");
    }

    private static async Task<IResult> SetSessionTimeZone(ClaimsPrincipal user,
        SetTimeZoneRequest request, TimeZonePreferenceService service,
        CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        var sessionId = GetGuidClaim(user, "sid");
        if (userId is null || sessionId is null) return Results.Unauthorized();
        try
        {
            return await service.SetSessionOverrideAsync(userId.Value, sessionId.Value,
                request.TimeZoneId, cancellationToken) ? Results.NoContent() : Results.Forbid();
        }
        catch (ArgumentException) { return Results.BadRequest(new { error = "invalid_time_zone" }); }
    }

    private static async Task<IResult> SetTenantTimeZone(Guid tenantId, ClaimsPrincipal user,
        SetTimeZoneRequest request, TimeZonePreferenceService service,
        CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.TimeZoneId))
            return Results.BadRequest(new { error = "invalid_time_zone" });
        try
        {
            return await service.SetTenantTimeZoneAsync(userId.Value, tenantId, request.TimeZoneId,
                cancellationToken) ? Results.NoContent() : Results.Forbid();
        }
        catch (ArgumentException) { return Results.BadRequest(new { error = "invalid_time_zone" }); }
    }

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value) ? value : null;
}

public sealed record SetTimeZoneRequest(string? TimeZoneId);
