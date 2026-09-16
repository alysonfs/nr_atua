namespace Atua.Api.Application.Identity;

public sealed class AuthOptions
{
    public const string SectionName = "Authentication";
    public string Issuer { get; set; } = "atua-api";
    public string Audience { get; set; } = "atua-browser";
    public string SigningKey { get; set; } = string.Empty;
    public int RefreshTokenLifetimeDays { get; set; } = 30;
}
