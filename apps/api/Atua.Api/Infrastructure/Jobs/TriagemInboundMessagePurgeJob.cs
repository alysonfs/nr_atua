using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atua.Api.Infrastructure.Jobs;

public sealed class TriagemInboundMessagePurgeJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<TriagemInboundMessagePurgeJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "TriagemInboundMessagePurgeJob: erro ao excluir mensagens expiradas.");
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

    private async Task PurgeExpiredMessagesAsync(CancellationToken cancellationToken)
    {
        var purgeThresholdUtc =
            TriagemInboundMessageRetentionPolicy.CalculatePurgeThresholdUtc(timeProvider.GetUtcNow());

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AtuaDbContext>();
            var removed = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                WITH expired_messages AS (
                    SELECT "Id"
                    FROM triagem_channel_messages
                    WHERE "ExpiresAtUtc" <= {purgeThresholdUtc}
                    ORDER BY "ExpiresAtUtc"
                    LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED
                )
                DELETE FROM triagem_channel_messages AS message
                USING expired_messages
                WHERE message."Id" = expired_messages."Id";
                """,
                cancellationToken);

            if (removed == 0)
            {
                break;
            }

            logger.LogInformation(
                "TriagemInboundMessagePurgeJob: {Count} mensagem(ns) excluída(s) neste lote.",
                removed);

            if (removed < BatchSize)
            {
                break;
            }
        }
    }
}
