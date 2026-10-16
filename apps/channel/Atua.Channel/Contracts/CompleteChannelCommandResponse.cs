namespace Atua.Channel.Contracts;

/// <summary>
/// Espelha <c>CompleteChannelCommandResponse</c> da Master API (ADR-042).
/// </summary>
public sealed record CompleteChannelCommandResponse(
    Guid CommandId,
    string Status,
    DateTimeOffset CompletedAtUtc);
