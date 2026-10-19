namespace Atua.Api.Domain.Triagens;

internal static class TriagemInboundMessageRetentionPolicy
{
    private const int CompleteRetentionYears = 5;
    private const int PostExpirationRetentionYears = 1;

    internal static DateTimeOffset CalculateExpirationUtc(DateTimeOffset receivedAtUtc) =>
        receivedAtUtc.AddYears(CompleteRetentionYears);

    internal static DateTimeOffset CalculatePurgeThresholdUtc(DateTimeOffset utcNow) =>
        utcNow.AddYears(-PostExpirationRetentionYears);
}
