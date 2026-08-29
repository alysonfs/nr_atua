using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Billing;

/// <summary>
/// Regra central de elegibilidade que a Master API deve expor ao coletor
/// autenticado. O coletor não consulta Trial ou Tenant diretamente.
/// </summary>
public sealed class TrialEligibilityService(AtuaDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<bool> IsTenantEligibleAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        return await dbContext.TrialSubscriptions
            .AsNoTracking()
            .AnyAsync(trial => trial.TenantId == tenantId && nowUtc < trial.ExpiresAt,
                cancellationToken);
    }
}
