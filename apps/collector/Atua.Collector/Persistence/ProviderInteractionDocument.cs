using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Atua.Collector.Persistence;

/// <summary>
/// Documento append-only da coleção <c>provider_interactions</c> (RF-022, ADR-028).
/// Um documento por chamada HTTP real do Worker ao provedor — login, listagem
/// (<c>list_query</c>) ou enriquecimento de detalhe (<c>detail_query</c>) — preservando
/// request/response brutos como unidade auditável completa. Substitui
/// <see cref="WorkOrderSnapshotDocument"/> (RF-016, superseded).
/// Nunca contém credenciais nem dados de sessão do provedor (RF-022.7) — sessão vive em
/// <c>provider_sessions</c> (RF-023).
/// </summary>
public sealed class ProviderInteractionDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Identificador do tenant (RF-022.3/RN-022.3).</summary>
    [BsonElement("tenant_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; init; }

    /// <summary>Identificador do provedor de origem (RF-022.3/RN-022.3). Ex.: "iservice".</summary>
    [BsonElement("provider_type")]
    public string ProviderType { get; init; } = string.Empty;

    /// <summary>Identificador do comando de coleta que gerou esta interação (RF-022.3/RN-022.3).</summary>
    [BsonElement("command_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid CommandId { get; init; }

    /// <summary>Tipo de interação: "login" | "list_query" | "detail_query" (RF-022.3/RN-022.3).</summary>
    [BsonElement("interaction_type")]
    public string InteractionType { get; init; } = string.Empty;

    /// <summary>
    /// Metadados da chamada feita ao provedor (ex.: página, status, workOrderId no caso de
    /// detail_query). NUNCA contém credenciais nem cookies de sessão (RF-022.7/RN-022.7).
    /// </summary>
    [BsonElement("request")]
    public BsonDocument Request { get; init; } = new();

    /// <summary>
    /// Payload bruto de OS retornado nesta interação, exatamente como recebido do provedor
    /// (RF-022.2/RN-022.2). Vazio para interações do tipo "login". Schema não formalizado
    /// (DP-022.1) — preservado sem transformação.
    /// </summary>
    [BsonElement("orders")]
    public BsonArray Orders { get; init; } = [];

    /// <summary>Se a interação foi concluída sem erro (RF-022.4/RN-022.4).</summary>
    [BsonElement("success")]
    public bool Success { get; init; }

    /// <summary>Mensagem de erro, se <see cref="Success"/> = false (RF-022.4/RN-022.4).</summary>
    [BsonElement("error_message")]
    public string? ErrorMessage { get; init; }

    /// <summary>Timestamp UTC de inserção (RF-022.5/RN-022.5) — nunca datas do provedor.</summary>
    [BsonElement("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}
