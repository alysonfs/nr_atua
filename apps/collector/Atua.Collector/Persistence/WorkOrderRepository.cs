using Atua.Collector.IService;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace Atua.Collector.Persistence;

/// <summary>
/// Implementação do repositório de snapshots de ordens de serviço usando MongoDB Atlas (RF-016).
///
/// Coleção: <c>work_order_snapshots</c> — append-only, um documento por OS por coleta.
/// Índice único em (tenant_id, provider_id, command_id) garante idempotência de re-execução
/// (DP-016.1): duplicata é ignorada silenciosamente, não falha o processo inteiro.
/// </summary>
public sealed class WorkOrderRepository(
    IMongoDatabase database,
    ILogger<WorkOrderRepository> logger)
    : IWorkOrderRepository
{
    private const string SnapshotsCollection = "work_order_snapshots";

    /// <summary>
    /// Cria os índices necessários se ainda não existirem.
    /// Idempotente — seguro chamar a cada startup.
    /// </summary>
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var collection = database.GetCollection<WorkOrderSnapshotDocument>(SnapshotsCollection);

        // Índice único (tenant_id, provider_id, command_id) — idempotência de re-execução (DP-016.1)
        var uniqueIndex = new CreateIndexModel<WorkOrderSnapshotDocument>(
            Builders<WorkOrderSnapshotDocument>.IndexKeys
                .Ascending(d => d.TenantId)
                .Ascending(d => d.ProviderId)
                .Ascending(d => d.CommandId),
            new CreateIndexOptions { Unique = true, Name = "idx_tenant_provider_command" });

        await collection.Indexes.CreateOneAsync(uniqueIndex, cancellationToken: cancellationToken);

        logger.LogDebug("[MONGO] Índices garantidos para {Collection}.", SnapshotsCollection);
    }

    /// <inheritdoc/>
    public async Task InsertSnapshotsAsync(
        Guid tenantId,
        Guid commandId,
        string providerType,
        CollectionResult result,
        CancellationToken cancellationToken = default)
    {
        var collection = database.GetCollection<WorkOrderSnapshotDocument>(SnapshotsCollection);
        var now = DateTimeOffset.UtcNow;

        var documents = new List<WorkOrderSnapshotDocument>();
        var skipCount = 0;

        foreach (var (_, orders) in result.OrdersByStatus)
        {
            foreach (var rawOrder in orders)
            {
                if (rawOrder is not IDictionary<string, object?> orderDict)
                {
                    skipCount++;
                    logger.LogWarning(
                        "[REPO] OS descartada: payload não é dicionário. CommandId={CommandId}.",
                        commandId);
                    continue;
                }

                var providerId = WorkOrderMapper.ExtractProviderOrderId(orderDict);

                if (string.IsNullOrWhiteSpace(providerId))
                {
                    skipCount++;
                    logger.LogWarning(
                        "[REPO] OS descartada: provider_id ausente/inválido (RF-016.5). CommandId={CommandId}.",
                        commandId);
                    continue;
                }

                // Converte o rawOrder para BsonDocument preservando todos os campos (RF-016.2)
                var rawDoc = new BsonDocument(
                    orderDict.Where(kv => kv.Value is not null)
                             .Select(kv => new BsonElement(kv.Key, BsonValue.Create(kv.Value))));

                documents.Add(new WorkOrderSnapshotDocument
                {
                    TenantId = tenantId,
                    ProviderId = providerId,
                    ProviderType = providerType,
                    CommandId = commandId,
                    RawData = rawDoc,
                    CreatedAt = now
                });
            }
        }

        if (documents.Count == 0)
        {
            logger.LogInformation(
                "[REPO] Nenhum snapshot a inserir. CommandId={CommandId} Descartadas={SkipCount}.",
                commandId, skipCount);
            return;
        }

        try
        {
            // IsOrdered=false: continua inserindo mesmo se um documento violar o índice único
            // (re-execução do mesmo commandId — DP-016.1). Duplicatas são ignoradas, não falham.
            await collection.InsertManyAsync(
                documents,
                new InsertManyOptions { IsOrdered = false },
                cancellationToken);

            logger.LogInformation(
                "[REPO] {Count} snapshot(s) inserido(s). TenantId={TenantId} CommandId={CommandId} Descartadas={SkipCount}.",
                documents.Count, tenantId, commandId, skipCount);
        }
        catch (MongoBulkWriteException ex) when (ex.WriteErrors.All(e => e.Category == ServerErrorCategory.DuplicateKey))
        {
            // Todos os erros são duplicatas: re-execução idempotente do mesmo commandId (DP-016.1)
            logger.LogInformation(
                "[REPO] InsertMany: {DupCount} duplicata(s) ignorada(s) por índice único. TenantId={TenantId} CommandId={CommandId}.",
                ex.WriteErrors.Count, tenantId, commandId);
        }
        catch (MongoBulkWriteException ex) when (ex.WriteErrors.Any(e => e.Category == ServerErrorCategory.DuplicateKey))
        {
            // Inserções parciais: parte dos docs foi inserida, parte eram duplicatas
            var inserted = documents.Count - ex.WriteErrors.Count;
            logger.LogInformation(
                "[REPO] InsertMany parcial: {Inserted} inserido(s), {DupCount} duplicata(s) ignorada(s). TenantId={TenantId} CommandId={CommandId}.",
                inserted, ex.WriteErrors.Count, tenantId, commandId);
        }
    }
}
