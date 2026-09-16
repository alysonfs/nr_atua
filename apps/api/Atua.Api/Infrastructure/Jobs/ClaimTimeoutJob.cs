using Atua.Api.Application.Integrations.CollectorControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atua.Api.Infrastructure.Jobs;

/// <summary>
/// Job de reconciliação de timeout de re-claim (ADR-021/D2).
///
/// Executa a cada 5 minutos e transita comandos <c>Claimed</c> com
/// <c>ClaimExpiresAtUtc &lt; now</c> para <c>Failed/ClaimTimeout</c>.
/// Garante a transição mesmo sem nova tentativa de claim pelo Worker.
///
/// A transição para <c>Failed/ClaimTimeout</c> <strong>não aciona</strong>
/// <c>ReconcileEligibilityAsync</c> — o Agente permanece <c>Active</c>
/// (ADR-021/D2).
/// </summary>
public sealed class ClaimTimeoutJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ClaimTimeoutJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider
                    .GetRequiredService<ImmediateCollectionCommandService>();
                var expired = await service.ExpireTimedOutCommandsAsync(stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation(
                        "ClaimTimeoutJob: {Count} comando(s) expirado(s) para Failed/ClaimTimeout.",
                        expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutdown normal
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ClaimTimeoutJob: erro ao expirar comandos com timeout de claim.");
            }
        }
    }
}
