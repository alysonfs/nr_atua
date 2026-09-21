using Atua.Collector.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atua.Collector.Consumer;

/// <summary>
/// Job de limpeza por marca d'água de <c>provider_interactions</c> (ADR-030, decisão 2).
/// Mecanismo autoritativo, desacoplado do Change Stream: cobre o resíduo de documentos já
/// confirmados como processados no Postgres que a exclusão inline
/// (<see cref="ProviderInteractionConsumerWorker.ProcessChangeAsync"/>) não conseguiu
/// remover — ex.: processo do Worker caiu entre o commit da transação Postgres e a chamada
/// a <see cref="IProviderInteractionRepository.DeleteProcessedAsync"/>. É seguro apagar
/// qualquer documento com <c>created_at &lt;= watermark</c>, pois a marca d'água só avança
/// depois do commit correspondente (<see cref="IWorkOrderPgRepository.GetLastProcessedInteractionCreatedAtAsync"/>).
/// Mesmo esqueleto de <c>ClaimTimeoutJob</c>/<c>RecurrentCollectionSchedulerJob</c> (ADR-029).
/// </summary>
public sealed class ProviderInteractionCleanupJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ProviderInteractionCleanupJob> logger)
    : BackgroundService
{
    private const string ConsumerId = "provider-interaction-to-work-order";
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var pgRepository = scope.ServiceProvider.GetRequiredService<IWorkOrderPgRepository>();
                var providerInteractionRepository = scope.ServiceProvider.GetRequiredService<IProviderInteractionRepository>();

                var watermark = await pgRepository.GetLastProcessedInteractionCreatedAtAsync(ConsumerId, stoppingToken);
                if (watermark is null)
                {
                    logger.LogDebug(
                        "[CLEANUP] Nenhuma marca d'água persistida ainda para '{ConsumerId}'. Nada a limpar.",
                        ConsumerId);
                    continue;
                }

                var deleted = await providerInteractionRepository.DeleteProcessedUpToAsync(watermark.Value, stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation(
                        "[CLEANUP] {Count} documento(s) de provider_interactions removido(s) (created_at <= {Watermark:o}).",
                        deleted, watermark.Value);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutdown normal
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[CLEANUP] Erro ao executar limpeza de provider_interactions.");
            }
        }
    }
}
