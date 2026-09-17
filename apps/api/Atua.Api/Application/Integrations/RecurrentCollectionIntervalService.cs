using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Integrations;

/// <summary>
/// Leitura/alteração do intervalo de coleta recorrente da integração
/// (RF-025). Autorização reaproveita a mesma regra de RF-008/ADR-020:
/// somente membership ativo OWNER/ADMIN no tenant.
/// </summary>
public sealed class RecurrentCollectionIntervalService(AtuaDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<RecurrentCollectionIntervalResult> GetAsync(Guid userId, Guid tenantId,
        Guid integrationId, CancellationToken cancellationToken)
    {
        if (!await HasControlRoleAsync(userId, tenantId, cancellationToken))
        {
            return RecurrentCollectionIntervalResult.Failure(ERecurrentCollectionIntervalStatus.Forbidden);
        }

        var integration = await dbContext.Integrations.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == integrationId && item.TenantId == tenantId, cancellationToken);
        if (integration is null)
        {
            return RecurrentCollectionIntervalResult.Failure(
                ERecurrentCollectionIntervalStatus.IntegrationNotFound);
        }

        return RecurrentCollectionIntervalResult.Success(integration.RecurrentCollectionIntervalMinutes);
    }

    /// <summary>
    /// RN-025.5: alterar o intervalo nunca ativa/desativa o Agente nem
    /// cancela comandos pendentes; apenas persiste o novo valor.
    /// </summary>
    public async Task<RecurrentCollectionIntervalResult> SetAsync(Guid userId, Guid tenantId,
        Guid integrationId, int minutes, CancellationToken cancellationToken)
    {
        if (!await HasControlRoleAsync(userId, tenantId, cancellationToken))
        {
            return RecurrentCollectionIntervalResult.Failure(ERecurrentCollectionIntervalStatus.Forbidden);
        }

        var integration = await dbContext.Integrations.SingleOrDefaultAsync(
            item => item.Id == integrationId && item.TenantId == tenantId, cancellationToken);
        if (integration is null)
        {
            return RecurrentCollectionIntervalResult.Failure(
                ERecurrentCollectionIntervalStatus.IntegrationNotFound);
        }

        if (minutes < Domain.Integrations.Integration.MinRecurrentCollectionIntervalMinutes ||
            minutes > Domain.Integrations.Integration.MaxRecurrentCollectionIntervalMinutes)
        {
            return RecurrentCollectionIntervalResult.Failure(
                ERecurrentCollectionIntervalStatus.InvalidInterval);
        }

        integration.SetRecurrentCollectionIntervalMinutes(minutes, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return RecurrentCollectionIntervalResult.Success(integration.RecurrentCollectionIntervalMinutes);
    }

    /// <summary>RF-025.3/RN-025.4: mesma regra de autorização de RF-008.</summary>
    private Task<bool> HasControlRoleAsync(Guid userId, Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.TenantMemberships.AsNoTracking().AnyAsync(membership =>
            membership.UserId == userId && membership.TenantId == tenantId &&
            (membership.Role == ETenantMembershipRole.Owner ||
             membership.Role == ETenantMembershipRole.Admin), cancellationToken);
}

public sealed record RecurrentCollectionIntervalView(int RecurrentCollectionIntervalMinutes);

public sealed record RecurrentCollectionIntervalResult(ERecurrentCollectionIntervalStatus Status,
    RecurrentCollectionIntervalView? View)
{
    public static RecurrentCollectionIntervalResult Success(int minutes) =>
        new(ERecurrentCollectionIntervalStatus.Success, new RecurrentCollectionIntervalView(minutes));

    public static RecurrentCollectionIntervalResult Failure(ERecurrentCollectionIntervalStatus status) =>
        new(status, null);
}
