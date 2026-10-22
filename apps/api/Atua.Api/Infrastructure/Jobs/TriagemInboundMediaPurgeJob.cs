using Atua.Api.Application.Triagens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atua.Api.Infrastructure.Jobs;

/// <summary>
/// <c>BackgroundService</c> que drena periodicamente a mídia inbound da
/// triagem elegível para expurgo físico (ADR-046/047), delegando a um lote
/// por vez a <see cref="TriagemInboundMediaPurgeService"/>. Segue o mesmo
/// padrão de <see cref="TriagemOutboxDrainWorker"/>/<see cref="TriagemInboundMessagePurgeJob"/>
/// já hospedados nesta Master API.
///
/// Dentro de um mesmo ciclo, continua reivindicando lotes até esgotar os
/// itens elegíveis (lote menor que <see cref="TriagemInboundMediaPurgeService.DefaultBatchSize"/>)
/// e então aguarda o próximo intervalo — evita manter um lote parcial
/// pendente por até 24h quando há um volume grande acumulado.
///
/// Cria um escopo de DI por lote (nunca reutiliza nem captura um
/// <c>AtuaDbContext</c> scoped através de iterações do loop, já que este
/// tipo é registrado como singleton pelo host).
/// </summary>
public sealed class TriagemInboundMediaPurgeJob(
    IServiceScopeFactory scopeFactory,
    ILogger<TriagemInboundMediaPurgeJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredMediaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "[TRIAGEM-INBOUND-MEDIA-PURGE] Erro inesperado ao expurgar mídia inbound.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PurgeExpiredMediaAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<TriagemInboundMediaPurgeService>();
            var result = await service.PurgeOnceAsync(
                TriagemInboundMediaPurgeService.DefaultBatchSize, cancellationToken);

            if (result.BatchSize < TriagemInboundMediaPurgeService.DefaultBatchSize
                || result.Purged < result.BatchSize)
            {
                break;
            }
        }
    }
}
