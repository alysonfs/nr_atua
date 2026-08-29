using Atua.Api.Domain.Tenants;

namespace Atua.Api.Application.Localization;

public static class TimeZoneContextResolver
{
    public const string PtBrDefaultTimeZoneId = "America/Sao_Paulo";
    public const string UtcTimeZoneId = "UTC";

    public static string Resolve(
        string? sessionTimeZoneOverrideId,
        string? tenantTimeZoneId,
        string? cultureName)
    {
        if (IanaTimeZone.IsValid(sessionTimeZoneOverrideId))
        {
            return sessionTimeZoneOverrideId!;
        }

        if (IanaTimeZone.IsValid(tenantTimeZoneId))
        {
            return tenantTimeZoneId!;
        }

        return string.Equals(cultureName, "pt-BR", StringComparison.OrdinalIgnoreCase)
            ? PtBrDefaultTimeZoneId
            : UtcTimeZoneId;
    }
}
