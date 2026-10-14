using Atua.Api.Application.Triagens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atua.Api.Infrastructure.Jobs;

/// <summary>
/// <c>BackgroundService</c> que drena periodicamente <c>triagem_trigger_outbox</c>
/// (ADR-041), chamando <see cref="StartTriagemUseCase"/> diretamente, em
/// processo — sem HTTP, sem credencial de serviço cross-tenant. Segue o
/// mesmo padrão de <see cref="ClaimTimeoutJob"/>/<see cref="RecurrentCollectionSchedulerJob"/>
/// já hospedados nesta Master API (ADR-029).
///
/// Cria um escopo de DI por lote (nunca reutiliza nem captura um
/// <c>AtuaDbContext</c> scoped através de iterações do loop, já que este
/// tipo é registrado como singleton pelo host).
/// </summary>
public sealed class TriagemOutboxDrainWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TriagemOutboxOptions> options,
    ILogger<TriagemOutboxDrainWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var drainService = scope.ServiceProvider.GetRequiredService<TriagemOutboxDrainService>();
                await drainService.DrainOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutdown normal — itens porventura em Processing permanecem
                // assim; a lease expira e os torna elegíveis a um novo claim.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[TRIAGEM-OUTBOX] Erro inesperado ao drenar lote.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollingIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutdown normal
            }
        }
    }
}
