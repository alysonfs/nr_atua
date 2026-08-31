using MongoDB.Bson;

namespace Atua.Collector.Consumer;

/// <summary>
/// Extrai campos estruturados do rawData de um snapshot (ADR-023, seção 3.2).
/// Uma implementação por provedor — selecionada pelo consumer via <c>provider_type</c>.
/// </summary>
public interface ISnapshotAdapter
{
    /// <summary>
    /// Extrai o status atual da OS a partir do rawData bruto do provedor.
    /// Retorna <c>null</c> se o campo de status estiver ausente ou vazio (RF-017.5).
    /// </summary>
    string? ExtractStatus(BsonDocument rawData);
}
