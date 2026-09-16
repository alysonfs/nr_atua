using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Atua.Collector.Consumer;

/// <summary>
/// Consumer de Change Streams sobre <c>provider_interactions</c> (Fase 4 do refactor de
/// persistência — docs/implementation/PLANO-refactor-provider-interactions-collector.md).
/// Roda como <see cref="IHostedService"/> adicional no processo do Worker Coletor.
/// Substitui <c>SnapshotConsumerWorker</c> (ADR-023, coleção <c>work_order_snapshots</c>,
/// superseded): a fonte agora é <c>provider_interactions</c>, um documento podendo conter N
/// OS (ex.: uma página inteira de <c>list_query</c>), em vez de 1 OS por documento.
/// Para cada interação inserida (exceto <c>login</c>, que não carrega OS):
/// - seleciona o <see cref="IProviderInteractionOrderAdapter"/> pelo campo <c>provider_type</c>;
/// - extrai (providerId, status) de cada OS em <c>orders</c> via o adapter;
/// - faz upsert em work_orders + append condicional em work_order_histories para cada OS;
/// - persiste um único resume token por interação processada, na mesma transação Postgres.
/// </summary>
public sealed class ProviderInteractionConsumerWorker(
    IMongoDatabase mongoDatabase,
    WorkOrderPgRepository pgRepository,
    IReadOnlyDictionary<string, IProviderInteractionOrderAdapter> adapters,
    ILogger<ProviderInteractionConsumerWorker> logger)
    : BackgroundService
{
    private const string InteractionsCollection = "provider_interactions";

    /// <summary>
    /// Identificador deste consumer em <c>consumer_states</c> — deliberadamente diferente de
    /// <c>snapshot-to-work-order</c> (consumer antigo), pois a nova fonte (<c>provider_interactions</c>)
    /// tem sua própria linha do tempo de resume tokens, não compatível com a antiga.
    /// </summary>
    private const string ConsumerId = "provider-interaction-to-work-order";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[CONSUMER] ProviderInteractionConsumerWorker iniciado.");

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
            catch (Exception ex) when (IsResumeTokenExpired(ex))
            {
                // O resume token persistido aponta para uma posição que o Atlas já reciclou
                // do oplog (código 286/ChangeStreamHistoryLost). Retomar com esse token
                // sempre falharia da mesma forma — limpamos o token e reabrimos o stream a
                // partir do ponto corrente (RN Fase 4). Não apaga nenhum dado de negócio.
                logger.LogWarning(ex,
                    "[CONSUMER] ResumeToken expirado (fora do oplog do Atlas). Limpando token " +
                    "persistido de '{ConsumerId}' e reabrindo o stream a partir do ponto corrente.",
                    ConsumerId);
                await pgRepository.ClearResumeTokenAsync(ConsumerId, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[CONSUMER] Erro no Change Stream. Aguardando {Delay}s antes de reconectar.",
                    RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        logger.LogInformation("[CONSUMER] ProviderInteractionConsumerWorker encerrado.");
    }

    /// <summary>
    /// Reconhece o erro de resume token expirado (fora do oplog). O driver pode reportar
    /// isso como <see cref="MongoCommandException"/> com código 286/CodeName
    /// <c>ChangeStreamHistoryLost</c>, ou embrulhado numa exceção genérica — checamos a
    /// mensagem como fallback defensivo.
    /// </summary>
    private static bool IsResumeTokenExpired(Exception ex)
    {
        if (ex is MongoCommandException mongoEx &&
            (mongoEx.Code == 286 ||
             string.Equals(mongoEx.CodeName, "ChangeStreamHistoryLost", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ex.Message.Contains("resume point", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("oplog", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunChangeStreamAsync(CancellationToken stoppingToken)
    {
        var collection = mongoDatabase.GetCollection<BsonDocument>(InteractionsCollection);

        var savedToken = await pgRepository.GetResumeTokenAsync(ConsumerId, stoppingToken);
        BsonDocument? resumeToken = savedToken is not null ? BsonDocument.Parse(savedToken) : null;

        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .Match(cs => cs.OperationType == ChangeStreamOperationType.Insert);

        var options = new ChangeStreamOptions
        {
            FullDocument = ChangeStreamFullDocumentOption.UpdateLookup,
            ResumeAfter = resumeToken
        };

        logger.LogInformation(
            "[CONSUMER] Abrindo Change Stream em '{Collection}'. ResumeToken={HasToken}",
            InteractionsCollection, resumeToken is not null ? "sim" : "null");

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

        var interactionId = doc.TryGetValue("_id", out var idVal)
            ? (Guid.TryParse(idVal.ToString(), out var parsedId) ? parsedId : Guid.Empty)
            : Guid.Empty;

        var tenantId = doc.TryGetValue("tenant_id", out var tidVal)
            ? (Guid.TryParse(tidVal.ToString(), out var parsedTid) ? parsedTid : Guid.Empty)
            : Guid.Empty;

        var commandId = doc.TryGetValue("command_id", out var cidVal)
            ? (Guid.TryParse(cidVal.ToString(), out var parsedCid) ? parsedCid : Guid.Empty)
            : Guid.Empty;

        var providerType = doc.TryGetValue("provider_type", out var ptVal) ? ptVal.ToString() : null;
        var interactionType = doc.TryGetValue("interaction_type", out var itVal) ? itVal.ToString() : null;

        // "login" não carrega OS — nada a projetar no Postgres, só avança o token.
        if (string.Equals(interactionType, "login", StringComparison.OrdinalIgnoreCase))
        {
            await pgRepository.AdvanceResumeTokenAsync(tokenJson, ConsumerId, stoppingToken);
            return;
        }

        var success = doc.TryGetValue("success", out var successVal) && successVal.IsBoolean && successVal.AsBoolean;
        if (!success)
        {
            logger.LogDebug(
                "[CONSUMER] Interação com success=false. Nada a projetar. " +
                "InteractionId={InteractionId} TenantId={TenantId} CommandId={CommandId} Type={Type}.",
                interactionId, tenantId, commandId, interactionType);
            await pgRepository.AdvanceResumeTokenAsync(tokenJson, ConsumerId, stoppingToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(providerType) || !adapters.TryGetValue(providerType, out var adapter))
        {
            logger.LogWarning(
                "[CONSUMER] Adapter não encontrado para provider_type={ProviderType}. " +
                "InteractionId={InteractionId} TenantId={TenantId} CommandId={CommandId}. Avançando token.",
                providerType, interactionId, tenantId, commandId);
            await pgRepository.AdvanceResumeTokenAsync(tokenJson, ConsumerId, stoppingToken);
            return;
        }

        var ordersArray = doc.TryGetValue("orders", out var ordersVal) && ordersVal is BsonArray arr
            ? arr
            : [];

        var orders = adapter.ExtractOrders(ordersArray);

        if (orders.Count == 0)
        {
            logger.LogDebug(
                "[CONSUMER] Nenhuma OS extraível desta interação (array vazio ou sem campos " +
                "utilizáveis). InteractionId={InteractionId} TenantId={TenantId} CommandId={CommandId}.",
                interactionId, tenantId, commandId);
            await pgRepository.AdvanceResumeTokenAsync(tokenJson, ConsumerId, stoppingToken);
            return;
        }

        await pgRepository.ProcessInteractionAsync(
            interactionId, tenantId, orders, tokenJson, ConsumerId, stoppingToken);

        logger.LogDebug(
            "[CONSUMER] Interação processada. InteractionId={InteractionId} TenantId={TenantId} " +
            "OrdersCount={OrdersCount}",
            interactionId, tenantId, orders.Count);
    }
}
