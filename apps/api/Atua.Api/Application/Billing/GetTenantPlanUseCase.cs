using Atua.Api.Domain.Billing;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Billing;

/// <summary>
/// RF-020.6: consulta do plano ativo do tenant (nome, gratuito ou não, dias
/// restantes, limites e quantidade em uso). Qualquer membership do tenant
/// pode consultar (RN-020.4 restringe apenas contratar/trocar/cancelar).
/// </summary>
public sealed class GetTenantPlanUseCase(AtuaDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<GetTenantPlanResult?> ExecuteAsync(Guid userId, Guid tenantId,
        CancellationToken cancellationToken)
    {
        var hasMembership = await dbContext.TenantMemberships.AsNoTracking().AnyAsync(
            membership => membership.UserId == userId && membership.TenantId == tenantId, cancellationToken);
        if (!hasMembership) return null;

        var active = await (from tenantPlan in dbContext.TenantPlans.AsNoTracking()
                             join plan in dbContext.Plans.AsNoTracking() on tenantPlan.PlanId equals plan.Id
                             where tenantPlan.TenantId == tenantId && tenantPlan.Status == EBillingStatus.Active
                             select new { tenantPlan, plan })
            .SingleOrDefaultAsync(cancellationToken);

        if (active is null)
        {
            // RN-020.5: um tenant nunca deveria ficar sem TenantPlan ativo.
            // Não lançamos exceção não tratada; devolvemos estado
            // inconsistente para o chamador decidir a resposta HTTP.
            return GetTenantPlanResult.Inconsistent();
        }

        var now = timeProvider.GetUtcNow();
        int? daysRemaining = active.tenantPlan.ExpiresAt is null
            ? null
            : Math.Max(0, (int)Math.Ceiling((active.tenantPlan.ExpiresAt.Value - now).TotalDays));

        var usedIntegrations = await dbContext.Integrations.AsNoTracking()
            .CountAsync(integration => integration.TenantId == tenantId && integration.IsEnabled,
                cancellationToken);
        var usedUsers = await dbContext.TenantMemberships.AsNoTracking()
            .CountAsync(membership => membership.TenantId == tenantId, cancellationToken);

        return GetTenantPlanResult.Success(active.plan.Name, active.plan.IsFree, daysRemaining,
            active.plan.MaxIntegrations, usedIntegrations, active.plan.MaxUsers, usedUsers,
            active.tenantPlan.Status.ToString());
    }
}

public sealed record GetTenantPlanResult(bool IsConsistent, string? PlanName, bool? IsFree,
    int? DaysRemaining, int? MaxIntegrations, int? UsedIntegrations, int? MaxUsers, int? UsedUsers,
    string? Status)
{
    public static GetTenantPlanResult Success(string planName, bool isFree, int? daysRemaining,
        int maxIntegrations, int usedIntegrations, int maxUsers, int usedUsers, string status) =>
        new(true, planName, isFree, daysRemaining, maxIntegrations, usedIntegrations, maxUsers, usedUsers,
            status);

    public static GetTenantPlanResult Inconsistent() =>
        new(false, null, null, null, null, null, null, null, null);
}
