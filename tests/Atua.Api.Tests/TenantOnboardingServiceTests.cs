using Atua.Api.Application.Tenants;
using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

public class TenantOnboardingServiceTests
{
    [Fact]
    public async Task CriaTenantMembershipOwnerEAssociaTrialEmTransacaoUnica()
    {
        await using var context = CreateContext();
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        context.Users.Add(user);
        var trial = new TrialSubscription(Guid.CreateVersion7(), user.Id,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));
        context.TrialSubscriptions.Add(trial);
        await context.SaveChangesAsync();

        var result = await new TenantOnboardingService(context).ExecuteAsync(user.Id, "Atua Refrigeração",
            "11122233000183", CancellationToken.None);

        Assert.Equal(ECreateTenantStatus.Success, result.Status);
        Assert.NotNull(result.TenantId);

        var membership = await context.TenantMemberships.SingleAsync();
        Assert.Equal(result.TenantId, membership.TenantId);
        Assert.Equal(user.Id, membership.UserId);
        Assert.Equal(ETenantMembershipRole.Owner, membership.Role);

        var persistedTrial = await context.TrialSubscriptions.SingleAsync();
        Assert.Equal(result.TenantId, persistedTrial.TenantId);
    }

    [Fact]
    public async Task RejeitaCnpjComFormatoInvalido()
    {
        await using var context = CreateContext();
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        context.Users.Add(user);
        context.TrialSubscriptions.Add(new TrialSubscription(Guid.CreateVersion7(), user.Id,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)));
        await context.SaveChangesAsync();

        var result = await new TenantOnboardingService(context).ExecuteAsync(user.Id, "Atua",
            "123", CancellationToken.None);

        Assert.Equal(ECreateTenantStatus.InvalidCnpj, result.Status);
        Assert.Empty(context.Tenants);
    }

    [Fact]
    public async Task RejeitaCnpjJaAssociadoAOutroTenant()
    {
        await using var context = CreateContext();
        var existingTenant = new Tenant(Guid.CreateVersion7(), "Outra Empresa", "11122233000183",
            "America/Sao_Paulo");
        context.Tenants.Add(existingTenant);
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        context.Users.Add(user);
        context.TrialSubscriptions.Add(new TrialSubscription(Guid.CreateVersion7(), user.Id,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)));
        await context.SaveChangesAsync();

        var result = await new TenantOnboardingService(context).ExecuteAsync(user.Id, "Minha Empresa",
            "11122233000183", CancellationToken.None);

        Assert.Equal(ECreateTenantStatus.CnpjAlreadyRegistered, result.Status);
    }

    [Fact]
    public async Task RejeitaQuandoUsuarioJaEOwnerDeOutroTenant()
    {
        await using var context = CreateContext();
        var existingTenant = new Tenant(Guid.CreateVersion7(), "Empresa A", "11122233000183",
            "America/Sao_Paulo");
        context.Tenants.Add(existingTenant);
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        context.Users.Add(user);
        context.TenantMemberships.Add(new TenantMembership(existingTenant.Id, user.Id,
            ETenantMembershipRole.Owner));
        context.TrialSubscriptions.Add(new TrialSubscription(Guid.CreateVersion7(), user.Id,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)));
        await context.SaveChangesAsync();

        var result = await new TenantOnboardingService(context).ExecuteAsync(user.Id, "Empresa B",
            "11122233000264", CancellationToken.None);

        Assert.Equal(ECreateTenantStatus.UserAlreadyHasTenant, result.Status);
    }

    [Fact]
    public async Task RejeitaQuandoUsuarioNaoPossuiTrial()
    {
        await using var context = CreateContext();
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var result = await new TenantOnboardingService(context).ExecuteAsync(user.Id, "Atua",
            "11122233000183", CancellationToken.None);

        Assert.Equal(ECreateTenantStatus.TrialNotFound, result.Status);
    }

    private static AtuaDbContext CreateContext() => new(new DbContextOptionsBuilder<AtuaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
