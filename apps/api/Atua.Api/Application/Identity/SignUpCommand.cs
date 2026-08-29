namespace Atua.Api.Application.Identity;

public sealed record SignUpCommand(string Email, string Password, string PasswordConfirmation);