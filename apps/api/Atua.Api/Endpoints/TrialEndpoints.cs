using System.Security.Claims;
using Atua.Api.Application.Billing;
using Atua.Api.Application.Identity;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Endpoints;

public static class TrialEndpoints
{
    public static void MapTrialEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/me/trial", GetUserTrial)
            .RequireAuthorization("BrowserSession");
        endpoints.MapPut("/api/sessions/current/timezone", SetSessionTimeZone)
            .RequireAuthorization("BrowserSession");
        endpoints.MapPut("/api/tenants/{tenantId:guid}/timezone", SetTenantTimeZone)
            .RequireAuthorization("BrowserSession");
        endpoints.MapGet("/api/internal/collector/eligibility", GetCollectorEligibility)
            .RequireAuthorization("CollectorEligibility");
    }

    private static async Task<IResult> GetUserTrial(ClaimsPrincipal user, AtuaDbContext db,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var userId = GetGuidClaim(user, "sub");
        if (userId is null) return Results.Unauthorized();
        var trial = await db.TrialSubscriptions.AsNoTracking().SingleOrDefaultAsync(
            item => item.UserId == userId, cancellationToken);
        if (trial is null) return Results.NotFound();
        var now = timeProvider.GetUtcNow();
        return Results.Ok(new GetUserTrialResponse(trial.Id, trial.ExpiresAt,
            Math.Max(0, (int)Math.Ceiling((trial.ExpiresAt - now).TotalDays)),
            now < trial.ExpiresAt ? "active" : "expired"));
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

    private static async Task<IResult> GetCollectorEligibility(ClaimsPrincipal user,
        TrialEligibilityService service, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var tenantId = GetGuidClaim(user, "tenant_id");
        if (tenantId is null) return Results.Unauthorized();
        var eligible = await service.IsTenantEligibleAsync(tenantId.Value, cancellationToken);
        return Results.Ok(new CollectorEligibilityResponse(eligible, timeProvider.GetUtcNow()));
    }

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value) ? value : null;
}

public sealed record GetUserTrialResponse(Guid TrialId, DateTimeOffset ExpiresAtUtc,
    int DaysRemaining, string Status);
public sealed record SetTimeZoneRequest(string? TimeZoneId);
public sealed record CollectorEligibilityResponse(bool Eligible, DateTimeOffset EvaluatedAtUtc);
