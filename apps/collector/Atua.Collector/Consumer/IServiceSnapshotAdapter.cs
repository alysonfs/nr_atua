using MongoDB.Bson;

namespace Atua.Collector.Consumer;

/// <summary>
/// Adaptador de snapshot para o provedor iService (ADR-023, seção 3.2 / DP-023.1).
/// Extrai o campo <c>woStatus</c> do rawData bruto.
/// </summary>
public sealed class IServiceSnapshotAdapter : ISnapshotAdapter
{
    /// <inheritdoc/>
    public string? ExtractStatus(BsonDocument rawData)
    {
        if (!rawData.TryGetValue("woStatus", out var value))
            return null;

        var status = value?.ToString();
        return string.IsNullOrWhiteSpace(status) ? null : status;
    }
}
