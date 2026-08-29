namespace Atua.Api.Application.Identity;

public sealed record SignUpResult(ESignUpStatus Status, Guid? ConfirmationId = null);