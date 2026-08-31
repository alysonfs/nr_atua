namespace Atua.Collector.Contracts;

public sealed record CompleteResponse(
    Guid CommandId,
    string Status,
    DateTimeOffset CompletedAtUtc);
