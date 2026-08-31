using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Atua.Collector.Persistence;

/// <summary>
/// Documento append-only da coleção <c>work_order_snapshots</c> (RF-016).
/// Cada coleta de uma OS gera um novo documento — sem upsert.
/// Índice único em (tenant_id, provider_id, command_id) garante idempotência
/// de re-execução (DP-016.1).
/// Pode conter PII (ADR-021/D5).
/// </summary>
public sealed class WorkOrderSnapshotDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Identificador do tenant (RF-016.3).</summary>
    [BsonElement("tenant_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; init; }

    /// <summary>
    /// Identificador externo da OS no provedor (RF-016.3).
    /// Isolado em <see cref="WorkOrderMapper"/> — nenhuma outra parte do código
    /// deve referenciar o campo do iService diretamente (RF-016.6).
    /// </summary>
    [BsonElement("provider_id")]
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>
    /// Identificador do provedor de origem do snapshot (RF-016.3, RF-016 RN-016.7).
    /// Usado pelo consumer (ADR-023) para selecionar o SnapshotAdapter correto.
    /// Ex.: "iservice".
    /// </summary>
    [BsonElement("provider_type")]
    public string ProviderType { get; init; } = string.Empty;

    /// <summary>Identificador do comando de coleta que gerou este snapshot (RF-016.3).</summary>
    [BsonElement("command_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid CommandId { get; init; }

    /// <summary>
    /// Dados brutos da OS conforme retornados pelo provedor (RF-016.2).
    /// Schema não formalizado — preservado integralmente sem mapeamento.
    /// </summary>
    [BsonElement("rawdata")]
    public BsonDocument RawData { get; init; } = new();

    /// <summary>Timestamp UTC de inserção (RF-016.4) — nunca usar datas do provedor aqui.</summary>
    [BsonElement("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}
