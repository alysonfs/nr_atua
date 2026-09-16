using Atua.Api.Domain.Billing;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Billing;

/// <summary>
/// Consulta os limites do plano ativo do tenant e o quanto já está em uso
/// (RF-020.4/RF-020.5/RF-021.4). Reutilizável pelos pontos de aplicação do
/// limite (ativação de integração, convite de usuário).
/// </summary>
public sealed class TenantPlanLimitService(AtuaDbContext dbContext)
{
    /// <summary>
    /// Verdadeiro quando o tenant já atingiu (ou não possui plano ativo — 
    /// RN-020.5, estado congelado) o limite de integrações ativas do plano.
    /// </summary>
    public async Task<bool> IsIntegrationLimitReachedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var plan = await GetActivePlanAsync(tenantId, cancellationToken);
        if (plan is null) return true;

        var activeIntegrations = await dbContext.Integrations.AsNoTracking()
            .CountAsync(integration => integration.TenantId == tenantId && integration.IsEnabled,
                cancellationToken);
        return activeIntegrations >= plan.MaxIntegrations;
    }

    /// <summary>
    /// Verdadeiro quando o tenant já atingiu (ou não possui plano ativo —
    /// RN-020.5, estado congelado) o limite de usuários (memberships) do
    /// plano.
    /// </summary>
    public async Task<bool> IsUserLimitReachedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var plan = await GetActivePlanAsync(tenantId, cancellationToken);
        if (plan is null) return true;

        var activeMemberships = await dbContext.TenantMemberships.AsNoTracking()
            .CountAsync(membership => membership.TenantId == tenantId, cancellationToken);
        return activeMemberships >= plan.MaxUsers;
    }

    private async Task<Plan?> GetActivePlanAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await (from tenantPlan in dbContext.TenantPlans.AsNoTracking()
               join plan in dbContext.Plans.AsNoTracking() on tenantPlan.PlanId equals plan.Id
               where tenantPlan.TenantId == tenantId && tenantPlan.Status == EBillingStatus.Active
               select plan)
            .SingleOrDefaultAsync(cancellationToken);
}
