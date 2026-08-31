using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Atua.Collector.Consumer;

/// <summary>
/// Consumer de Change Streams sobre <c>work_order_snapshots</c> (ADR-023).
/// Roda como <see cref="IHostedService"/> adicional no processo do Worker Coletor.
/// Para cada snapshot inserido:
/// - seleciona o <see cref="ISnapshotAdapter"/> pelo campo <c>provider_type</c>;
/// - extrai o status via adapter;
/// - se ausente: loga Warning e avança o resume token;
/// - se presente: faz upsert em work_orders, append condicional em
///   work_order_histories e persiste o resume token, na mesma transação Postgres.
/// </summary>
public sealed class SnapshotConsumerWorker(
    IMongoDatabase mongoDatabase,
    WorkOrderPgRepository pgRepository,
    IReadOnlyDictionary<string, ISnapshotAdapter> adapters,
    ILogger<SnapshotConsumerWorker> logger)
    : BackgroundService
{
    private const string SnapshotsCollection = "work_order_snapshots";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[CONSUMER] SnapshotConsumerWorker iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunChangeStreamAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[CONSUMER] Erro no Change Stream. Aguardando {Delay}s antes de reconectar.",
                    RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        logger.LogInformation("[CONSUMER] SnapshotConsumerWorker encerrado.");
    }

    private async Task RunChangeStreamAsync(CancellationToken stoppingToken)
    {
        var collection = mongoDatabase.GetCollection<BsonDocument>(SnapshotsCollection);

        // Retoma do token persistido, se houver (ADR-023 seção 4.1)
        var savedToken = await pgRepository.GetResumeTokenAsync(stoppingToken);
        BsonDocument? resumeToken = savedToken is not null
            ? BsonDocument.Parse(savedToken)
            : null;

        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .Match(cs => cs.OperationType == ChangeStreamOperationType.Insert);

        var options = new ChangeStreamOptions
        {
            FullDocument = ChangeStreamFullDocumentOption.UpdateLookup,
            ResumeAfter = resumeToken
        };

        logger.LogInformation(
            "[CONSUMER] Abrindo Change Stream. ResumeToken={HasToken}",
            resumeToken is not null ? "sim" : "null");

        using var cursor = await collection.WatchAsync(pipeline, options, stoppingToken);

        await cursor.ForEachAsync(async change =>
        {
            if (stoppingToken.IsCancellationRequested) return;

            await ProcessChangeAsync(change, stoppingToken);
        }, stoppingToken);
    }

    private async Task ProcessChangeAsync(
        ChangeStreamDocument<BsonDocument> change,
        CancellationToken stoppingToken)
    {
        var doc = change.FullDocument;
        var tokenJson = change.ResumeToken.ToJson();

        // Extrai campos do documento (RF-016.3)
        var snapshotId = doc.TryGetValue("_id", out var idVal)
            ? (Guid.TryParse(idVal.ToString(), out var parsedId) ? parsedId : Guid.Empty)
            : Guid.Empty;

        var tenantId = doc.TryGetValue("tenant_id", out var tidVal)
            ? (Guid.TryParse(tidVal.ToString(), out var parsedTid) ? parsedTid : Guid.Empty)
            : Guid.Empty;

        var commandId = doc.TryGetValue("command_id", out var cidVal)
            ? (Guid.TryParse(cidVal.ToString(), out var parsedCid) ? parsedCid : Guid.Empty)
            : Guid.Empty;

        var providerId = doc.TryGetValue("provider_id", out var pidVal)
            ? pidVal.ToString()
            : null;

        var providerType = doc.TryGetValue("provider_type", out var ptVal)
            ? ptVal.ToString()
            : null;

        // Seleciona adapter pelo provider_type (ADR-023 seção 3.2)
        if (string.IsNullOrWhiteSpace(providerType) ||
            !adapters.TryGetValue(providerType, out var adapter))
        {
            logger.LogWarning(
                "[CONSUMER] Adapter não encontrado para provider_type={ProviderType}. " +
                "SnapshotId={SnapshotId} TenantId={TenantId} CommandId={CommandId}. Avançando token.",
                providerType, snapshotId, tenantId, commandId);
            await pgRepository.AdvanceResumeTokenAsync(tokenJson, stoppingToken);
            return;
        }

        var rawData = doc.TryGetValue("rawdata", out var rdVal) && rdVal is BsonDocument rdDoc
            ? rdDoc
            : new BsonDocument();

        var status = adapter.ExtractStatus(rawData);

        // Status ausente — descarte com log (RF-017.5 / ADR-023 seção 4.4)
        if (string.IsNullOrWhiteSpace(status))
        {
            logger.LogWarning(
                "[CONSUMER] Status ausente no rawdata. Descartando snapshot sem escrita no Postgres. " +
                "SnapshotId={SnapshotId} TenantId={TenantId} CommandId={CommandId}. Avançando token.",
                snapshotId, tenantId, commandId);
            await pgRepository.AdvanceResumeTokenAsync(tokenJson, stoppingToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(providerId))
        {
            logger.LogWarning(
                "[CONSUMER] provider_id ausente no documento. Descartando. " +
                "SnapshotId={SnapshotId} TenantId={TenantId} CommandId={CommandId}.",
                snapshotId, tenantId, commandId);
            await pgRepository.AdvanceResumeTokenAsync(tokenJson, stoppingToken);
            return;
        }

        // Processa no PostgreSQL (upsert + history + resume token na mesma tx)
        await pgRepository.ProcessSnapshotAsync(
            snapshotId, tenantId, providerId!, status, tokenJson, stoppingToken);

        logger.LogDebug(
            "[CONSUMER] Snapshot processado. SnapshotId={SnapshotId} TenantId={TenantId} " +
            "ProviderId={ProviderId} Status={Status}",
            snapshotId, tenantId, providerId, status);
    }
}
