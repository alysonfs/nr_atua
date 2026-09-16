using System.Security.Claims;
using Atua.Api.Application.Identity;

namespace Atua.Api.Endpoints;

public static class UserLocaleEndpoints
{
    public static void MapUserLocaleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/me/locale", GetLocale)
            .RequireAuthorization("BrowserSession");
        endpoints.MapPut("/api/users/me/locale", SetLocale)
            .RequireAuthorization("BrowserSession");
    }

    private static async Task<IResult> GetLocale(ClaimsPrincipal user,
        UserLocalePreferenceService service, CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId is null) return Results.Unauthorized();

        var locale = await service.GetAsync(userId.Value, cancellationToken);
        return locale is null
            ? Results.NotFound(new { error = "user_not_found" })
            : Results.Ok(new UserLocaleResponse(locale));
    }

    private static async Task<IResult> SetLocale(ClaimsPrincipal user, SetUserLocaleRequest request,
        UserLocalePreferenceService service, CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId is null) return Results.Unauthorized();

        try
        {
            var updated = await service.UpdateAsync(userId.Value, request.Locale,
                cancellationToken);
            return updated
                ? Results.NoContent()
                : Results.NotFound(new { error = "user_not_found" });
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "invalid_locale" });
        }
    }

    private static Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var userId) ? userId : null;
}

public sealed record SetUserLocaleRequest(string? Locale);

public sealed record UserLocaleResponse(string Locale);
