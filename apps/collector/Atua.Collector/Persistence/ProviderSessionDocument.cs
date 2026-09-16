using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Atua.Collector.Persistence;

/// <summary>
/// Documento mutável (upsert, não append-only) da coleção <c>provider_sessions</c>
/// (RF-023, ADR-028). No máximo um documento por <c>(tenant_id, provider_type)</c>
/// (RF-023.5/RN-023.5) — sobrescrito a cada novo login bem-sucedido ou invalidação.
/// Nunca observado por Change Stream nem exposto por API pública (RF-023.6/RN-023.6).
/// </summary>
public sealed class ProviderSessionDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Identificador do tenant (RF-023, chave natural junto com <see cref="ProviderType"/>).</summary>
    [BsonElement("tenant_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; init; }

    /// <summary>Identificador do provedor. Ex.: "iservice".</summary>
    [BsonElement("provider_type")]
    public string ProviderType { get; init; } = string.Empty;

    /// <summary>
    /// Estado de sessão do <c>BrowserContext</c> do Playwright (cookies + origins),
    /// serializado como JSON (equivalente ao retorno de <c>context.StorageStateAsync()</c>).
    /// Nunca exposto em log de diagnóstico local nem em <c>provider_interactions</c>
    /// (RF-023.6/RN-023.6).
    /// </summary>
    [BsonElement("storage_state")]
    public string StorageState { get; init; } = string.Empty;

    /// <summary>Se a sessão é considerada utilizável no momento (RF-023.1/RN-023.1).</summary>
    [BsonElement("valid")]
    public bool Valid { get; init; }

    /// <summary>
    /// Expiração estimada (TTL conservador, DP-023.1 — valor inicial a calibrar por
    /// observação real).
    /// </summary>
    [BsonElement("expires_at")]
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Instante do login que originou esta sessão.</summary>
    [BsonElement("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Instante da última atualização deste documento.</summary>
    [BsonElement("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }
}
