using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Lógica de um ciclo de drenagem da outbox do gatilho de triagem (ADR-041).
/// Hospedada por <c>TriagemOutboxDrainWorker</c> (<c>BackgroundService</c>),
/// mas extraída para uma classe de aplicação comum, testável sem
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> nem PostgreSQL
/// real — o claim em lote (<see cref="ITriagemOutboxRepository.ClaimBatchAsync"/>)
/// é a única parte que exige um banco relacional real (ver
/// <c>TriagemOutboxRepository</c>/<c>TriagemOutboxPersistenceSchema</c>).
///
/// Chama <see cref="StartTriagemUseCase"/> diretamente, em processo — nunca
/// HTTP —, e nunca a rota manual <c>POST /api/internal/triagens/start</c>.
/// </summary>
public sealed class TriagemOutboxDrainService(
    ITriagemOutboxRepository repository,
    StartTriagemUseCase useCase,
    IOptions<TriagemOutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<TriagemOutboxDrainService> logger)
{
    /// <summary>
    /// Reivindica e processa um único lote. Retorna um resumo para
    /// observabilidade/testes. Cancelamento via <paramref name="cancellationToken"/>
    /// nunca marca o item corrente como falha — ele permanece <c>Processing</c>
    /// e volta a ser elegível quando a lease expirar.
    /// </summary>
    public async Task<TriagemOutboxDrainBatchResult> DrainOnceAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var now = timeProvider.GetUtcNow();

        var batch = await repository.ClaimBatchAsync(
            opts.BatchSize, TimeSpan.FromSeconds(opts.LeaseSeconds), now, cancellationToken);

        if (batch.Count == 0)
        {
            return TriagemOutboxDrainBatchResult.Empty;
        }

        logger.LogInformation("[TRIAGEM-OUTBOX] Lote reivindicado. BatchSize={BatchSize}", batch.Count);

        var processed = 0;
        var skipped = 0;
        var retried = 0;
        var failed = 0;

        foreach (var item in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Itens restantes do lote permanecem Processing: a lease já
                // reivindicada expira naturalmente e os torna elegíveis para
                // um novo claim — nunca marcados como falha por cancelamento.
                logger.LogInformation(
                    "[TRIAGEM-OUTBOX] Drenagem cancelada; itens restantes do lote permanecem Processing. OutboxId={OutboxId}",
                    item.Id);
                break;
            }

            try
            {
                var result = await useCase.ExecuteAsync(
                    item.TenantId, item.IntegrationId, item.WorkOrderProviderId, cancellationToken);

                switch (result.Outcome)
                {
                    case EStartTriagemOutcome.Created or EStartTriagemOutcome.Existing:
                        item.MarkProcessed(timeProvider.GetUtcNow());
                        processed++;
                        logger.LogInformation(
                            "[TRIAGEM-OUTBOX] Item processado. OutboxId={OutboxId} Outcome={Outcome}",
                            item.Id, result.Outcome);
                        break;

                    case EStartTriagemOutcome.WorkOrderNotFound:
                        item.MarkSkipped(ETriagemOutboxErrorCode.WorkOrderNotFound, timeProvider.GetUtcNow());
                        skipped++;
                        logger.LogWarning(
                            "[TRIAGEM-OUTBOX] Item ignorado (OS não encontrada na revalidação). OutboxId={OutboxId}",
                            item.Id);
                        break;

                    case EStartTriagemOutcome.WorkOrderNotEligible:
                        item.MarkSkipped(ETriagemOutboxErrorCode.WorkOrderNotEligible, timeProvider.GetUtcNow());
                        skipped++;
                        logger.LogWarning(
                            "[TRIAGEM-OUTBOX] Item ignorado (OS não elegível na revalidação). OutboxId={OutboxId}",
                            item.Id);
                        break;

                    default:
                        throw new InvalidOperationException($"Outcome não tratado: {result.Outcome}.");
                }

                await repository.SaveChangesAsync(CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown durante o processamento deste item: nunca marcado
                // como falha — permanece Processing, lease expira para
                // reprocessamento (requisito do ADR-041).
                logger.LogInformation(
                    "[TRIAGEM-OUTBOX] Processamento cancelado; item permanece Processing. OutboxId={OutboxId}",
                    item.Id);
                break;
            }
            catch (Exception ex)
            {
                var errorCode = ClassifyError(ex);
                var failureNow = timeProvider.GetUtcNow();
                var prospectiveAttemptCount = item.AttemptCount + 1;
                var nextAttemptAtUtc = TriagemOutboxRetryPolicy.ComputeNextAttemptAtUtc(
                    failureNow, prospectiveAttemptCount, opts.BaseRetrySeconds, opts.MaxRetrySeconds);

                item.RecordFailure(errorCode, failureNow, opts.MaxAttempts, nextAttemptAtUtc);

                if (item.Status == ETriagemTriggerOutboxStatus.Failed)
                {
                    failed++;
                    logger.LogError(ex,
                        "[TRIAGEM-OUTBOX] Item atingiu o máximo de tentativas; marcado Failed. " +
                        "OutboxId={OutboxId} AttemptCount={AttemptCount} ErrorCode={ErrorCode}",
                        item.Id, item.AttemptCount, errorCode);
                }
                else
                {
                    retried++;
                    logger.LogWarning(ex,
                        "[TRIAGEM-OUTBOX] Falha ao processar item; nova tentativa agendada. " +
                        "OutboxId={OutboxId} AttemptCount={AttemptCount} NextAttemptAtUtc={NextAttemptAtUtc} ErrorCode={ErrorCode}",
                        item.Id, item.AttemptCount, item.NextAttemptAtUtc, errorCode);
                }

                await repository.SaveChangesAsync(CancellationToken.None);
            }
        }

        return new TriagemOutboxDrainBatchResult(batch.Count, processed, skipped, retried, failed);
    }

    /// <summary>
    /// Classifica a exceção num código seguro (nunca <c>Exception.Message</c>
    /// — ver <see cref="ETriagemOutboxErrorCode"/>). Nesta fase não há
    /// classificação específica de falha transitória conhecida (ex.: timeout
    /// de rede/banco); toda exceção não tratada explicitamente pelos outcomes
    /// de <see cref="StartTriagemUseCase"/> acima é <see cref="ETriagemOutboxErrorCode.Unclassified"/>
    /// — tratada da mesma forma que uma falha transitória (retry com
    /// backoff), nunca mascarada como sucesso.
    /// </summary>
    private static ETriagemOutboxErrorCode ClassifyError(Exception exception) => exception switch
    {
        TimeoutException => ETriagemOutboxErrorCode.TransientError,
        _ => ETriagemOutboxErrorCode.Unclassified,
    };
}

/// <summary>Resumo de um ciclo de drenagem — observabilidade/testes.</summary>
public sealed record TriagemOutboxDrainBatchResult(int BatchSize, int Processed, int Skipped, int Retried, int Failed)
{
    public static readonly TriagemOutboxDrainBatchResult Empty = new(0, 0, 0, 0, 0);
}
