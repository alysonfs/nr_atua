namespace Atua.Api.Domain.Tenants;

public static class IanaTimeZone
{
    public static bool IsValid(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) ||
            (timeZoneId != "UTC" && !timeZoneId.Contains('/', StringComparison.Ordinal)))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
