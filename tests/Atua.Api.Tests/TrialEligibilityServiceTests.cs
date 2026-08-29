using Atua.Api.Application.Billing;
using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Identity;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

public class TrialEligibilityServiceTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task ConsideraAtivoSomenteAntesDoVencimento(int secondsFromExpiry, bool expected)
    {
        var now = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
        var tenantId = Guid.CreateVersion7();
        await using var context = CreateContext();
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        context.Users.Add(user);
        var trial = new TrialSubscription(Guid.CreateVersion7(), user.Id, now.AddDays(-7),
            now.AddSeconds(secondsFromExpiry));
        trial.AssociateWithTenant(tenantId);
        context.TrialSubscriptions.Add(trial);
        await context.SaveChangesAsync();

        var eligible = await new TrialEligibilityService(context, new FixedTimeProvider(now))
            .IsTenantEligibleAsync(tenantId, CancellationToken.None);

        Assert.Equal(expected, eligible);
    }

    private static AtuaDbContext CreateContext() => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
