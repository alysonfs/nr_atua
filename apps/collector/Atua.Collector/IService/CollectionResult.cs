namespace Atua.Collector.IService;

/// <summary>
/// Resultado de uma coleta de ordens de serviço no iService.
/// </summary>
public sealed record CollectionResult(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyDictionary<string, int> StatusCounts,
    IReadOnlyDictionary<string, IReadOnlyList<object>> OrdersByStatus);
