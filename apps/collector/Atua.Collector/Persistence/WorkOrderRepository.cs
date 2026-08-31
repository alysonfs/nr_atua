using Atua.Collector.IService;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace Atua.Collector.Persistence;

/// <summary>
/// Implementação do repositório de ordens de serviço usando MongoDB Atlas.
///
/// Coleções (ADR-021):
/// - <c>work_order_snapshots</c>: estado atual de cada OS — upsert idempotente por (tenantId, providerOrderId).
/// - <c>work_order_observations</c>: observação imutável por OS por coleta — índice único em (tenantId, providerOrderId, commandId).
///
/// A inicialização dos índices é feita em <see cref="EnsureIndexesAsync"/> e deve
/// ser chamada uma vez na inicialização da aplicação.
/// </summary>
public sealed class WorkOrderRepository(
    IMongoDatabase database,
    ILogger<WorkOrderRepository> logger)
    : IWorkOrderRepository
{
    private const string SnapshotsCollection = "work_order_snapshots";
    private const string ObservationsCollection = "work_order_observations";

    /// <summary>
    /// Cria os índices necessários se ainda não existirem.
    /// Idempotente — seguro chamar a cada startup.
    /// </summary>
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        // work_order_snapshots: índice de idempotência (tenantId, providerOrderId)
        var snapshots = database.GetCollection<WorkOrderSnapshotDocument>(SnapshotsCollection);
        var snapshotIndex = new CreateIndexModel<WorkOrderSnapshotDocument>(
            Builders<WorkOrderSnapshotDocument>.IndexKeys
                .Ascending(d => d.TenantId)
                .Ascending(d => d.ProviderOrderId),
            new CreateIndexOptions { Unique = true, Name = "idx_tenant_provider_order" });

        await snapshots.Indexes.CreateOneAsync(snapshotIndex, cancellationToken: cancellationToken);

        // work_order_observations: índice de idempotência (tenantId, providerOrderId, commandId)
        var observations = database.GetCollection<WorkOrderObservationDocument>(ObservationsCollection);
        var observationIndex = new CreateIndexModel<WorkOrderObservationDocument>(
            Builders<WorkOrderObservationDocument>.IndexKeys
                .Ascending(d => d.TenantId)
                .Ascending(d => d.ProviderOrderId)
                .Ascending(d => d.CommandId),
            new CreateIndexOptions { Unique = true, Name = "idx_tenant_provider_order_command" });

        await observations.Indexes.CreateOneAsync(observationIndex, cancellationToken: cancellationToken);

        logger.LogDebug("[MONGO] Índices garantidos para {Snapshots} e {Observations}.",
            SnapshotsCollection, ObservationsCollection);
    }

    /// <inheritdoc/>
    public async Task UpsertSnapshotsAsync(
        Guid tenantId,
        Guid commandId,
        CollectionResult result,
        CancellationToken cancellationToken = default)
    {
        var collection = database.GetCollection<WorkOrderSnapshotDocument>(SnapshotsCollection);
        var now = DateTimeOffset.UtcNow;
        var upsertCount = 0;
        var skipCount = 0;

        foreach (var (_, orders) in result.OrdersByStatus)
        {
            foreach (var rawOrder in orders)
            {
                var rawDict = ToStringObjectDict(rawOrder);
                var providerOrderId = WorkOrderMapper.ExtractProviderOrderId(rawDict);

                if (providerOrderId is null)
                {
                    logger.LogWarning(
                        "[MONGO] OS sem providerOrderId ignorada no upsert de snapshots. CommandId={CommandId}",
                        commandId);
                    skipCount++;
                    continue;
                }

                var rawBson = BsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(rawDict));

                var filter = Builders<WorkOrderSnapshotDocument>.Filter.And(
                    Builders<WorkOrderSnapshotDocument>.Filter.Eq(d => d.TenantId, tenantId),
                    Builders<WorkOrderSnapshotDocument>.Filter.Eq(d => d.ProviderOrderId, providerOrderId));

                var update = Builders<WorkOrderSnapshotDocument>.Update
                    .SetOnInsert(d => d.Id, Guid.CreateVersion7())
                    .SetOnInsert(d => d.TenantId, tenantId)
                    .SetOnInsert(d => d.ProviderOrderId, providerOrderId)
                    .Set(d => d.CommandId, commandId)
                    .Set(d => d.CapturedAtUtc, result.CapturedAtUtc)
                    .Set(d => d.UpdatedAtUtc, now)
                    .Set(d => d.RawData, rawBson);

                await collection.UpdateOneAsync(
                    filter, update,
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken);

                upsertCount++;
            }
        }

        logger.LogInformation(
            "[MONGO] Upsert de snapshots concluído. TenantId={TenantId} CommandId={CommandId} Upserted={Upserted} Skipped={Skipped}",
            tenantId, commandId, upsertCount, skipCount);
    }

    /// <inheritdoc/>
    public async Task InsertObservationsAsync(
        Guid tenantId,
        Guid commandId,
        CollectionResult result,
        CancellationToken cancellationToken = default)
    {
        var collection = database.GetCollection<WorkOrderObservationDocument>(ObservationsCollection);
        var docs = new List<WorkOrderObservationDocument>();

        foreach (var (_, orders) in result.OrdersByStatus)
        {
            foreach (var rawOrder in orders)
            {
                var rawDict = ToStringObjectDict(rawOrder);
                var providerOrderId = WorkOrderMapper.ExtractProviderOrderId(rawDict);

                if (providerOrderId is null)
                {
                    logger.LogWarning(
                        "[MONGO] OS sem providerOrderId ignorada na inserção de observações. CommandId={CommandId}",
                        commandId);
                    continue;
                }

                var rawBson = BsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(rawDict));

                docs.Add(new WorkOrderObservationDocument
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    ProviderOrderId = providerOrderId,
                    CommandId = commandId,
                    CapturedAtUtc = result.CapturedAtUtc,
                    RawData = rawBson
                });
            }
        }

        if (docs.Count == 0)
        {
            logger.LogInformation(
                "[MONGO] Nenhuma observação a inserir. TenantId={TenantId} CommandId={CommandId}",
                tenantId, commandId);
            return;
        }

        // InsertMany com ordered:false — continua mesmo que algum documento duplique
        // o índice único (coleta re-executada para o mesmo commandId).
        await collection.InsertManyAsync(
            docs,
            new InsertManyOptions { IsOrdered = false },
            cancellationToken);

        logger.LogInformation(
            "[MONGO] Observações inseridas. TenantId={TenantId} CommandId={CommandId} Count={Count}",
            tenantId, commandId, docs.Count);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Dictionary<string, object?> ToStringObjectDict(object rawOrder)
    {
        if (rawOrder is Dictionary<string, object?> dict)
            return dict;

        // Fallback: serializa/deserializa via JSON para garantir IDictionary<string, object?>
        var json = System.Text.Json.JsonSerializer.Serialize(rawOrder);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
               ?? new Dictionary<string, object?>();
    }
}
