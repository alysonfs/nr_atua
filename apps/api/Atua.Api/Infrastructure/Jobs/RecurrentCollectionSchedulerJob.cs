using Atua.Api.Application.Integrations.CollectorControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atua.Api.Infrastructure.Jobs;

/// <summary>
/// Agendador recorrente de coleta (RF-025/ADR-029).
///
/// Acorda a cada 1 minuto — mais fino que o mínimo de negócio de 5 minutos
/// (RF-025.4) para que o atraso entre "o intervalo venceu" e "o comando é
/// criado" seja desprezível — e cria novos <c>ImmediateCollectionCommand</c>s
/// <c>Pending</c> para integrações Ativas cujo intervalo configurado tenha
/// vencido.
/// </summary>
public sealed class RecurrentCollectionSchedulerJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RecurrentCollectionSchedulerJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider
                    .GetRequiredService<RecurrentCollectionSchedulerService>();
                var created = await service.ScheduleDueCollectionsAsync(stoppingToken);
                if (created > 0)
                {
                    logger.LogInformation(
                        "RecurrentCollectionSchedulerJob: {Count} comando(s) de coleta recorrente criado(s).",
                        created);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutdown normal
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RecurrentCollectionSchedulerJob: erro ao agendar coletas recorrentes.");
            }
        }
    }
}
