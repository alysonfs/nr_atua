namespace Atua.Collector.Contracts;

public sealed record CompleteRequest(
    string Outcome,
    string? FailureReason,
    DateTimeOffset? CompletedAtUtc);
