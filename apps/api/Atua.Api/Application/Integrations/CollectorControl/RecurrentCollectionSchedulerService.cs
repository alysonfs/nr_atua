using Atua.Api.Domain.Integrations.CollectorControl;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Integrations.CollectorControl;

/// <summary>
/// Agendador de coleta recorrente (RF-025/ADR-029). Consultado
/// periodicamente por <see cref="Infrastructure.Jobs.RecurrentCollectionSchedulerJob"/>.
///
/// Para cada integração com <c>CollectorActivation.Status == Active</c>, cria
/// um novo <see cref="ImmediateCollectionCommand"/> <c>Pending</c> quando o
/// último comando tiver vencido (<c>RequestedAtUtc + RecurrentCollectionIntervalMinutes
/// &lt;= now</c>) e não houver comando <c>Pending</c>/<c>Claimed</c> em aberto.
/// O intervalo é lido a cada varredura, nunca cacheado (RF-025.7).
/// </summary>
public sealed class RecurrentCollectionSchedulerService(AtuaDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<int> ScheduleDueCollectionsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // RF-025.6/RF-025.8: apenas integrações Ativas entram na varredura.
        var activeIntegrations = await dbContext.CollectorActivations.AsNoTracking()
            .Where(activation => activation.Status == ECollectorActivationStatus.Active)
            .Join(dbContext.Integrations.AsNoTracking(),
                activation => activation.IntegrationId, integration => integration.Id,
                (activation, integration) => new
                {
                    activation.TenantId,
                    activation.IntegrationId,
                    integration.ProviderId,
                    integration.RecurrentCollectionIntervalMinutes
                })
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var item in activeIntegrations)
        {
            var hasOpenCommand = await dbContext.ImmediateCollectionCommands.AsNoTracking()
                .AnyAsync(command => command.IntegrationId == item.IntegrationId &&
                    (command.Status == EImmediateCollectionCommandStatus.Pending ||
                     command.Status == EImmediateCollectionCommandStatus.Claimed), cancellationToken);
            if (hasOpenCommand) continue;

            var lastCommand = await dbContext.ImmediateCollectionCommands.AsNoTracking()
                .Where(command => command.IntegrationId == item.IntegrationId)
                .OrderByDescending(command => command.RequestedAtUtc).ThenByDescending(command => command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            // Integração recém-ativada: ActivateAsync já cria o primeiro
            // comando sincronamente (RF-008/ADR-020); o job assume a partir
            // do segundo ciclo (ADR-029).
            if (lastCommand is null) continue;

            var dueAtUtc = lastCommand.RequestedAtUtc.AddMinutes(item.RecurrentCollectionIntervalMinutes);
            if (dueAtUtc > now) continue;

            dbContext.ImmediateCollectionCommands.Add(new ImmediateCollectionCommand(
                Guid.CreateVersion7(), item.TenantId, item.IntegrationId, item.ProviderId, now));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                created++;
            }
            catch (DbUpdateException)
            {
                // ADR-029/ADR-020: o índice único parcial de Pending por
                // integração é a garantia final contra duplicação; uma
                // eventual violação é no-op, nunca erro fatal do job.
                dbContext.ChangeTracker.Clear();
            }
        }

        return created;
    }
}
