using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Integrations;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Integrations.CollectorControl;

/// <summary>
/// Implementação local (mesma fronteira transacional) de
/// <see cref="ICollectorEligibilityEvaluator"/>, conforme ADR-020/ADR-024.
///
/// Verifica apenas o <see cref="TenantPlan"/> ativo e não expirado do tenant.
/// Não bloqueia mais por <c>ValidationStatus</c> da credencial iService: o
/// status de validação continua sendo lido apenas para informar o usuário
/// (ADR-024).
/// </summary>
public sealed class CollectorEligibilityEvaluator(AtuaDbContext dbContext, TimeProvider timeProvider)
    : ICollectorEligibilityEvaluator
{
    public async Task<CollectorEligibility> EvaluateAsync(Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken)
    {
        // Isolamento: a credencial só é considerada se pertencer ao mesmo
        // tenant da rota (RN-008.1).
        var credentialStatus = await dbContext.IServiceCredentials.AsNoTracking()
            .Where(item => item.IntegrationId == integrationId && item.TenantId == tenantId)
            .Select(item => (EIServiceValidationStatus?)item.ValidationStatus)
            .SingleOrDefaultAsync(cancellationToken);

        var validationStatus = credentialStatus ?? EIServiceValidationStatus.NotValidated;

        var now = timeProvider.GetUtcNow();
        var hasActivePlan = await dbContext.TenantPlans.AsNoTracking().AnyAsync(
            tenantPlan => tenantPlan.TenantId == tenantId && tenantPlan.Status == EBillingStatus.Active &&
                          (tenantPlan.ExpiresAt == null || tenantPlan.ExpiresAt > now), cancellationToken);

        if (!hasActivePlan)
        {
            // RN-008.3/RF-020.7: plano inelegível (ausente/expirado) bloqueia
            // antes de qualquer outra consideração.
            return CollectorEligibility.Blocked(EActivationBlockReason.PlanIneligible, validationStatus);
        }

        return CollectorEligibility.Allowed(validationStatus);
    }
}
