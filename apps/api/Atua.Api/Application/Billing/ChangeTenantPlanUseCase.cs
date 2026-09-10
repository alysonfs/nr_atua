using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Billing;

/// <summary>
/// RN-020.4/RF-020.3: somente o membership <c>Owner</c> pode contratar,
/// trocar ou cancelar o plano do tenant. Trocar o plano encerra o vínculo
/// ativo anterior como <c>Superseded</c> e cria um novo vínculo ativo,
/// preservando o histórico (RN-020.2).
/// </summary>
public sealed class ChangeTenantPlanUseCase(AtuaDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<EChangeTenantPlanStatus> ExecuteAsync(Guid userId, Guid tenantId, string newPlanCode,
        CancellationToken cancellationToken)
    {
        var role = await dbContext.TenantMemberships.AsNoTracking()
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (ETenantMembershipRole?)membership.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (role is null or not ETenantMembershipRole.Owner)
        {
            return EChangeTenantPlanStatus.Forbidden;
        }

        var newPlan = await dbContext.Plans.SingleOrDefaultAsync(
            plan => plan.Code == newPlanCode && plan.IsActive, cancellationToken);
        if (newPlan is null)
        {
            return EChangeTenantPlanStatus.PlanNotFound;
        }

        var currentActive = await dbContext.TenantPlans.SingleOrDefaultAsync(
            tenantPlan => tenantPlan.TenantId == tenantId && tenantPlan.Status == EBillingStatus.Active,
            cancellationToken);
        if (currentActive is null)
        {
            // RN-020.5: não deveria ocorrer; tratado como estado inconsistente.
            return EChangeTenantPlanStatus.NoActivePlan;
        }

        var now = timeProvider.GetUtcNow();
        currentActive.Supersede(now);

        var expiresAt = newPlan.DurationDays is null
            ? (DateTimeOffset?)null
            : new DateTimeOffset(now.UtcDateTime.Date.AddDays(newPlan.DurationDays.Value), TimeSpan.Zero);
        dbContext.TenantPlans.Add(new TenantPlan(Guid.CreateVersion7(), tenantId, newPlan.Id, now, expiresAt));

        await dbContext.SaveChangesAsync(cancellationToken);
        return EChangeTenantPlanStatus.Success;
    }
}

public enum EChangeTenantPlanStatus
{
    Success,
    Forbidden,
    PlanNotFound,
    NoActivePlan
}
