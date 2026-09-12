using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Atua.Collector.Persistence;

/// <summary>
/// Implementação do repositório de sessões do provedor usando MongoDB Atlas
/// (RF-023, ADR-028).
///
/// Coleção: <c>provider_sessions</c> — mutável, no máximo um documento por
/// <c>(tenant_id, provider_type)</c>, garantido por índice único + <c>ReplaceOneAsync</c>
/// com <c>IsUpsert = true</c> (não é append-only, diferente de
/// <see cref="ProviderInteractionRepository"/>).
/// </summary>
public sealed class ProviderSessionRepository(
    IMongoDatabase database,
    ILogger<ProviderSessionRepository> logger)
    : IProviderSessionRepository
{
    private const string SessionsCollection = "provider_sessions";

    private static IMongoCollection<ProviderSessionDocument> Collection(IMongoDatabase database) =>
        database.GetCollection<ProviderSessionDocument>(SessionsCollection);

    /// <inheritdoc/>
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var collection = Collection(database);

        // Índice único (tenant_id, provider_type) — garante no máximo um documento por
        // chave (RF-023.5/RN-023.5), usado como filtro do upsert em SaveValidSessionAsync.
        var uniqueIndex = new CreateIndexModel<ProviderSessionDocument>(
            Builders<ProviderSessionDocument>.IndexKeys
                .Ascending(d => d.TenantId)
                .Ascending(d => d.ProviderType),
            new CreateIndexOptions { Unique = true, Name = "idx_tenant_provider_unique" });

        await collection.Indexes.CreateOneAsync(uniqueIndex, cancellationToken: cancellationToken);

        logger.LogDebug("[MONGO] Índices garantidos para {Collection}.", SessionsCollection);
    }

    /// <inheritdoc/>
    public async Task<string?> GetValidStorageStateAsync(
        Guid tenantId, string providerType, CancellationToken cancellationToken = default)
    {
        var filter = Builders<ProviderSessionDocument>.Filter.Where(d =>
            d.TenantId == tenantId
            && d.ProviderType == providerType
            && d.Valid
            && d.ExpiresAt > DateTimeOffset.UtcNow);

        var document = await Collection(database)
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);

        return document?.StorageState;
    }

    /// <inheritdoc/>
    public async Task SaveValidSessionAsync(
        Guid tenantId,
        string providerType,
        string storageState,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var filter = Builders<ProviderSessionDocument>.Filter.Where(d =>
            d.TenantId == tenantId && d.ProviderType == providerType);

        var replacement = new ProviderSessionDocument
        {
            TenantId = tenantId,
            ProviderType = providerType,
            StorageState = storageState,
            Valid = true,
            ExpiresAt = now.Add(ttl),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await Collection(database).ReplaceOneAsync(
            filter, replacement, new ReplaceOptions { IsUpsert = true }, cancellationToken);

        logger.LogDebug(
            "[REPO] Sessão salva para provedor '{ProviderType}'. TenantId={TenantId} ExpiresAt={ExpiresAt}.",
            providerType, tenantId, replacement.ExpiresAt);
    }

    /// <inheritdoc/>
    public async Task InvalidateSessionAsync(
        Guid tenantId, string providerType, CancellationToken cancellationToken = default)
    {
        var filter = Builders<ProviderSessionDocument>.Filter.Where(d =>
            d.TenantId == tenantId && d.ProviderType == providerType);

        var update = Builders<ProviderSessionDocument>.Update
            .Set(d => d.Valid, false)
            .Set(d => d.UpdatedAt, DateTimeOffset.UtcNow);

        var result = await Collection(database).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

        logger.LogDebug(
            "[REPO] Sessão invalidada para provedor '{ProviderType}'. TenantId={TenantId} MatchedCount={MatchedCount}.",
            providerType, tenantId, result.MatchedCount);
    }
}
