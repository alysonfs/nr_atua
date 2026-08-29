using Atua.Api.Application.Identity;

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
    }
}

public sealed record SignUpRequest(string Email, string Password, string PasswordConfirmation);