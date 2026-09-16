using Atua.Api.Application.Identity;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

public class TimeZonePreferenceServiceTests
{
    [Fact]
    public async Task NaoAlteraFusoDeTenantSemMembership()
    {
        await using var context = new AtuaDbContext(new DbContextOptionsBuilder<AtuaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new Tenant(Guid.CreateVersion7(), "Atua", "12345678901234", "America/Sao_Paulo");
        var member = new User(Guid.CreateVersion7(), null, "member@atua.com", "hash");
        var outsider = new User(Guid.CreateVersion7(), null, "outsider@atua.com", "hash");
        context.AddRange(tenant, member, outsider);
        context.TenantMemberships.Add(new TenantMembership(tenant.Id, member.Id,
            ETenantMembershipRole.Admin));
        await context.SaveChangesAsync();

        var changed = await new TimeZonePreferenceService(context).SetTenantTimeZoneAsync(
            outsider.Id, tenant.Id, "Europe/Lisbon", CancellationToken.None);

        Assert.False(changed);
        Assert.Equal("America/Sao_Paulo", (await context.Tenants.FindAsync(tenant.Id))!.TimeZoneId);
    }
}
