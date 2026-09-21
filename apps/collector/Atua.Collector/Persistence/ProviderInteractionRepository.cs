using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace Atua.Collector.Persistence;

/// <summary>
/// Implementação do repositório de interações brutas com o provedor usando MongoDB Atlas
/// (RF-022, ADR-028).
///
/// Coleção: <c>provider_interactions</c> — append-only, um documento por chamada HTTP real
/// ao provedor (login, listagem, detalhe). Sem índice único: re-execuções geram novas
/// interações reais (diferente do antigo repositório de <c>work_order_snapshots</c>,
/// removido no cutover da Fase 5, que deduplicava por
/// OS/comando) — cada chamada HTTP é, por definição, uma nova interação auditável.
/// </summary>
public sealed class ProviderInteractionRepository(
    IMongoDatabase database,
    ILogger<ProviderInteractionRepository> logger)
    : IProviderInteractionRepository
{
    private const string InteractionsCollection = "provider_interactions";

    /// <inheritdoc/>
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var collection = database.GetCollection<ProviderInteractionDocument>(InteractionsCollection);

        // Índice de consulta (não único) por tenant/comando/tempo — suporta auditoria e
        // futura leitura pelo Office (RF futuro) sem forçar full scan.
        var queryIndex = new CreateIndexModel<ProviderInteractionDocument>(
            Builders<ProviderInteractionDocument>.IndexKeys
                .Ascending(d => d.TenantId)
                .Ascending(d => d.CommandId)
                .Descending(d => d.CreatedAt),
            new CreateIndexOptions { Name = "idx_tenant_command_created" });

        await collection.Indexes.CreateOneAsync(queryIndex, cancellationToken: cancellationToken);

        logger.LogDebug("[MONGO] Índices garantidos para {Collection}.", InteractionsCollection);
    }

    /// <inheritdoc/>
    public async Task InsertInteractionAsync(
        Guid tenantId,
        Guid commandId,
        string providerType,
        string interactionType,
        IReadOnlyDictionary<string, object?> request,
        IReadOnlyList<object?>? orders,
        bool success,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        var collection = database.GetCollection<ProviderInteractionDocument>(InteractionsCollection);

        var document = new ProviderInteractionDocument
        {
            TenantId = tenantId,
            CommandId = commandId,
            ProviderType = providerType,
            InteractionType = interactionType,
            Request = BuildBsonDocument(request),
            Orders = BuildBsonArray(orders),
            Success = success,
            ErrorMessage = errorMessage,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);

            logger.LogDebug(
                "[REPO] Interação '{InteractionType}' registrada. TenantId={TenantId} CommandId={CommandId} Success={Success}.",
                interactionType, tenantId, commandId, success);
        }
        catch (Exception ex)
        {
            // Falha ao registrar a interação NÃO deve derrubar o ciclo de coleta — o log de
            // auditoria é um complemento, não pode se tornar ele mesmo uma nova causa de
            // falha do Worker (mesma postura de robustez adotada pelo debug logger local).
            logger.LogWarning(ex,
                "[REPO] Falha ao registrar interação '{InteractionType}' em {Collection}. TenantId={TenantId} CommandId={CommandId}.",
                interactionType, InteractionsCollection, tenantId, commandId);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteProcessedAsync(Guid interactionId, CancellationToken cancellationToken = default)
    {
        var collection = database.GetCollection<ProviderInteractionDocument>(InteractionsCollection);

        try
        {
            await collection.DeleteOneAsync(d => d.Id == interactionId, cancellationToken);

            logger.LogDebug(
                "[REPO] Interação processada removida de {Collection}. InteractionId={InteractionId}.",
                InteractionsCollection, interactionId);
        }
        catch (Exception ex)
        {
            // Best-effort (ADR-030, decisão 1): falha ao apagar não pode derrubar o consumer
            // nem bloquear a projeção já confirmada — o ProviderInteractionCleanupJob cobre o
            // resíduo de documentos órfãos.
            logger.LogWarning(ex,
                "[REPO] Falha ao remover interação processada de {Collection}. InteractionId={InteractionId}.",
                InteractionsCollection, interactionId);
        }
    }

    /// <inheritdoc/>
    public async Task<long> DeleteProcessedUpToAsync(DateTimeOffset watermark, CancellationToken cancellationToken = default)
    {
        var collection = database.GetCollection<ProviderInteractionDocument>(InteractionsCollection);

        var filter = Builders<ProviderInteractionDocument>.Filter.Lte(d => d.CreatedAt, watermark);
        var result = await collection.DeleteManyAsync(filter, cancellationToken);

        return result.DeletedCount;
    }

    /// <summary>
    /// Converte um dicionário bruto em <see cref="BsonDocument"/> preservando integralmente
    /// todos os campos (inclusive aninhados).
    /// </summary>
    private static BsonDocument BuildBsonDocument(IReadOnlyDictionary<string, object?> dict)
    {
        return new BsonDocument(
            dict.Select(kv => new BsonElement(
                kv.Key,
                kv.Value is null ? BsonNull.Value : BsonValue.Create(kv.Value))));
    }

    /// <summary>
    /// Converte uma lista bruta em <see cref="BsonArray"/> preservando integralmente cada
    /// item (RF-022.2/RN-022.2). Retorna array vazio para "login" (sem OS envolvidas).
    /// </summary>
    private static BsonArray BuildBsonArray(IReadOnlyList<object?>? items)
    {
        if (items is null || items.Count == 0) return [];

        return new BsonArray(items.Select(item => item is null ? BsonNull.Value : BsonValue.Create(item)));
    }
}
