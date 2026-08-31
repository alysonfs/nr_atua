using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Atua.Collector.Persistence;

/// <summary>
/// Documento da coleção <c>work_order_observations</c>.
/// Uma observação imutável por OS por coleta.
/// Índice único em (tenantId, providerOrderId, commandId).
/// Pode conter PII (ADR-021/D5).
/// </summary>
public sealed class WorkOrderObservationDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Identificador do tenant — indexado (ADR-021/D5).</summary>
    [BsonElement("tenantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; init; }

    /// <summary>
    /// Chave externa do iService — campo provisório: workOrderNo (ADR-021/D7).
    /// // TODO(D7): substituir workOrderNo por workOrderId após descoberta do iService real.
    /// Isolado em <see cref="WorkOrderMapper"/> — nenhuma outra parte do código deve
    /// referenciar o campo do iService diretamente.
    /// </summary>
    [BsonElement("providerOrderId")]
    public string ProviderOrderId { get; init; } = string.Empty;

    /// <summary>Identificador do comando de coleta — parte do índice de idempotência.</summary>
    [BsonElement("commandId")]
    [BsonRepresentation(BsonType.String)]
    public Guid CommandId { get; init; }

    /// <summary>Momento UTC da captura — imutável após inserção.</summary>
    [BsonElement("capturedAtUtc")]
    public DateTimeOffset CapturedAtUtc { get; init; }

    /// <summary>
    /// Dados brutos da OS conforme retornados pelo iService.
    /// Schema não formalizado — preservado como documento dinâmico.
    /// </summary>
    [BsonElement("rawData")]
    public BsonDocument RawData { get; init; } = new();
}
