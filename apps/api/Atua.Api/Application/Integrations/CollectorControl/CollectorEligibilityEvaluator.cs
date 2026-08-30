using Atua.Api.Application.Billing;
using Atua.Api.Domain.Integrations;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Integrations.CollectorControl;

/// <summary>
/// Implementação local (mesma fronteira transacional) de
/// <see cref="ICollectorEligibilityEvaluator"/>, conforme ADR-020.
///
/// Reusa <see cref="TrialEligibilityService"/> (ADR-015) como regra única de
/// Trial e lê o <c>ValidationStatus</c> vigente da credencial iService da
/// integração (ADR-018/RF-007).
/// </summary>
public sealed class CollectorEligibilityEvaluator(
    AtuaDbContext dbContext,
    TrialEligibilityService trialEligibilityService)
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

        var trialEligible = await trialEligibilityService.IsTenantEligibleAsync(tenantId, cancellationToken);
        if (!trialEligible)
        {
            // RN-008.3: Trial inelegível bloqueia antes de qualquer outra
            // consideração.
            return CollectorEligibility.Blocked(EActivationBlockReason.TrialIneligible, validationStatus);
        }

        if (validationStatus != EIServiceValidationStatus.Succeeded)
        {
            // RN-008.3: credencial ausente ou sem validação Succeeded.
            return CollectorEligibility.Blocked(EActivationBlockReason.CredentialsNotValidated,
                validationStatus);
        }

        return CollectorEligibility.Allowed(validationStatus);
    }
}
