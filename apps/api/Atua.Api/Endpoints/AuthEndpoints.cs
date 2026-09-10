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

        endpoints.MapPost("/auth/resend-confirmation", async (ResendConfirmationRequest request,
            SignUpService service, CancellationToken cancellationToken) =>
        {
            await service.ResendConfirmationAsync(request.Email, cancellationToken);
            // Sempre 200: não revela se o e-mail existe ou já foi confirmado.
            return Results.Ok();
        })
        .WithName("ResendConfirmation")
        .Produces(StatusCodes.Status200OK);

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

        endpoints.MapPost("/auth/signin", async (SignInRequest body, AuthService service,
            HttpRequest request, HttpResponse response, IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            var tokens = await service.SignInAsync(body.Email, body.Password, cancellationToken);
            if (tokens is null) return Results.Unauthorized();
            SetRefreshCookie(request, response, tokens.RefreshToken, environment);
            return Results.Ok(new AccessTokenResponse(tokens.AccessToken));
        });

        endpoints.MapPost("/auth/refresh", async (HttpRequest request, HttpResponse response,
                AuthService service, IWebHostEnvironment environment, CancellationToken cancellationToken) =>
        {
            if (!request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken))
                return Results.Unauthorized();
            var tokens = await service.RefreshAsync(refreshToken, cancellationToken);
            if (tokens is null)
            {
                DeleteRefreshCookie(request, response, environment);
                return Results.Unauthorized();
            }
            SetRefreshCookie(request, response, tokens.RefreshToken, environment);
            return Results.Ok(new AccessTokenResponse(tokens.AccessToken));
        });

        endpoints.MapPost("/auth/signout", async (ClaimsPrincipal user, AuthService service,
            HttpRequest request, HttpResponse response, IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            if (Guid.TryParse(user.FindFirstValue("sid"), out var sessionId))
                await service.SignOutAsync(sessionId, cancellationToken);
            DeleteRefreshCookie(request, response, environment);
            return Results.NoContent();
        }).RequireAuthorization("BrowserSession");
    }

    private const string RefreshCookieName = "atua_refresh";

    private static void SetRefreshCookie(HttpRequest request, HttpResponse response,
        string refreshToken, IWebHostEnvironment environment)
    {
        var localDevelopment = IsLocalDevelopmentRequest(request, environment);
        // OPÇÃO D (aprovada pelo usuário, 2026-09-01 — débito técnico documentado):
        // Em dev local, Office e API rodam em portas diferentes; SameSite=Lax permite
        // restaurar sessão sem exigir HTTPS. Produção continua Secure + Strict.
        response.Cookies.Append(RefreshCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !localDevelopment,
            SameSite = localDevelopment ? SameSiteMode.Lax : SameSiteMode.Strict,
            Path = "/auth"
        });
    }

    private static void DeleteRefreshCookie(HttpRequest request, HttpResponse response,
        IWebHostEnvironment environment)
    {
        var localDevelopment = IsLocalDevelopmentRequest(request, environment);
        response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            Secure = !localDevelopment,
            SameSite = localDevelopment ? SameSiteMode.Lax : SameSiteMode.Strict,
            Path = "/auth"
        });
    }

    private static bool IsLocalDevelopmentRequest(HttpRequest request, IWebHostEnvironment environment)
    {
        var host = request.Host.Host;
        return environment.IsDevelopment()
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record SignUpRequest(string Email, string Password, string PasswordConfirmation);

public sealed record ResendConfirmationRequest(string Email);

public sealed record ConfirmEmailRequest(string Email, string Code);
public sealed record SignInRequest(string Email, string Password);
public sealed record AccessTokenResponse(string AccessToken);