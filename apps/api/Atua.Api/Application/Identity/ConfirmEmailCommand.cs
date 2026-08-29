namespace Atua.Api.Application.Identity;

public sealed record ConfirmEmailCommand(string Email, string Code);
