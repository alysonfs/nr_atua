using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Identity;

public sealed class TimeZonePreferenceService(AtuaDbContext dbContext)
{
    public async Task<bool> SetSessionOverrideAsync(Guid userId, Guid sessionId,
        string? timeZoneId, CancellationToken cancellationToken)
    {
        var session = await dbContext.AuthSessions.SingleOrDefaultAsync(
            item => item.Id == sessionId && item.UserId == userId && item.RevokedAt == null,
            cancellationToken);
        if (session is null) return false;
        session.SetTimeZoneOverride(timeZoneId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetTenantTimeZoneAsync(Guid userId, Guid tenantId,
        string timeZoneId, CancellationToken cancellationToken)
    {
        var membership = await dbContext.TenantMemberships.AnyAsync(item =>
            item.UserId == userId && item.TenantId == tenantId, cancellationToken);
        if (!membership) return false;
        var tenant = await dbContext.Tenants.FindAsync(new object?[] { tenantId }, cancellationToken);
        if (tenant is null) return false;
        tenant.SetTimeZone(timeZoneId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
