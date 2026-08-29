using Atua.Api.Application.Identity;
using System.Security.Claims;

namespace Atua.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/signup", async (SignUpRequest request,
            SignUpService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ExecuteAsync(new SignUpCommand(request.Email,
                request.Password, request.PasswordConfirmation), cancellationToken);

            return result.Status switch
            {
                ESignUpStatus.Success => Results.Accepted(),
                ESignUpStatus.EmailAlreadyRegistered => Results.Conflict(new
                {
                    error = "email_already_registered"
                }),
                ESignUpStatus.InvalidEmail => Results.BadRequest(new
                {
                    error = "invalid_email"
                }),
                ESignUpStatus.InvalidPassword => Results.BadRequest(new
                {
                    error = "invalid_password"
                }),
                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            };
        })
        .WithName("SignUp")
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);

        endpoints.MapPost("/auth/confirm-email", async (ConfirmEmailRequest request,
            ConfirmEmailService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ExecuteAsync(new ConfirmEmailCommand(request.Email,
                request.Code), cancellationToken);

            return result.Status switch
            {
                EConfirmEmailStatus.Success => Results.Ok(),
                EConfirmEmailStatus.AlreadyConfirmed => Results.Conflict(new
                {
                    error = "email_already_confirmed"
                }),
                EConfirmEmailStatus.InvalidCode => Results.BadRequest(new
                {
                    error = "invalid_code"
                }),
                EConfirmEmailStatus.ExpiredCode => Results.BadRequest(new
                {
                    error = "expired_code"
                }),
                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            };
        })
        .WithName("ConfirmEmail")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);

        endpoints.MapPost("/auth/signin", async (SignInRequest request, AuthService service,
            HttpResponse response, CancellationToken cancellationToken) =>
        {
            var tokens = await service.SignInAsync(request.Email, request.Password, cancellationToken);
            if (tokens is null) return Results.Unauthorized();
            SetRefreshCookie(response, tokens.RefreshToken);
            return Results.Ok(new AccessTokenResponse(tokens.AccessToken));
        });

        endpoints.MapPost("/auth/refresh", async (HttpRequest request, HttpResponse response,
            AuthService service, CancellationToken cancellationToken) =>
        {
            if (!request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken))
                return Results.Unauthorized();
            var tokens = await service.RefreshAsync(refreshToken, cancellationToken);
            if (tokens is null)
            {
                response.Cookies.Delete(RefreshCookieName);
                return Results.Unauthorized();
            }
            SetRefreshCookie(response, tokens.RefreshToken);
            return Results.Ok(new AccessTokenResponse(tokens.AccessToken));
        });

        endpoints.MapPost("/auth/signout", async (ClaimsPrincipal user, AuthService service,
            HttpResponse response, CancellationToken cancellationToken) =>
        {
            if (Guid.TryParse(user.FindFirstValue("sid"), out var sessionId))
                await service.SignOutAsync(sessionId, cancellationToken);
            response.Cookies.Delete(RefreshCookieName);
            return Results.NoContent();
        }).RequireAuthorization("BrowserSession");
    }

    private const string RefreshCookieName = "atua_refresh";

    private static void SetRefreshCookie(HttpResponse response, string refreshToken) =>
        response.Cookies.Append(RefreshCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/auth"
        });
}

public sealed record SignUpRequest(string Email, string Password, string PasswordConfirmation);

public sealed record ConfirmEmailRequest(string Email, string Code);
public sealed record SignInRequest(string Email, string Password);
public sealed record AccessTokenResponse(string AccessToken);