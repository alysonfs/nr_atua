namespace Atua.Api.Tests;

using Atua.Api.Domain;
using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;

public class TenancyTests
{
    [Fact]
    public void UsuarioPodeParticiparDeDoisTenants()
    {
        var user = new User(Uuid7.New(), "Leonardo", "owner@atua.com",
            "$argon2id$v=19$hash", EGlobalUserRole.User);
        var firstMembership = new TenantMembership(Uuid7.New(), user.Id,
            ETenantMembershipRole.Owner);
        var secondMembership = new TenantMembership(Uuid7.New(), user.Id,
            ETenantMembershipRole.Admin);

        Assert.Equal(user.Id, firstMembership.UserId);
        Assert.Equal(ETenantMembershipRole.Owner, firstMembership.Role);
        Assert.Equal(user.Id, secondMembership.UserId);
        Assert.Equal(ETenantMembershipRole.Admin, secondMembership.Role);
    }

    [Fact]
    public void RootEhUmaPermissaoGlobal()
    {
        var root = new User(Uuid7.New(), "Administrador", "root@atua.com",
            "$argon2id$v=19$hash", EGlobalUserRole.Root);

        Assert.Equal(EGlobalUserRole.Root, root.GlobalRole);
    }

    [Fact]
    public void TenantRepresentaEmpresa()
    {
        var tenant = new Tenant(Uuid7.New(), "Natal Refrigeracao",
            "53353865000106", "America/Sao_Paulo");

        Assert.Equal("Natal Refrigeracao", tenant.Name);
        Assert.Equal("53353865000106", tenant.Cnpj);
    }

    [Fact]
    public void IntegracaoVinculaTenantAoProvedor()
    {
        var provider = new IntegrationProvider(Uuid7.New(), "iService", "Midea",
            new Uri("https://ics-amer.midea.com"), true);
        var integration = new Integration(Uuid7.New(), Uuid7.New(), provider.Id, false);

        Assert.Equal(provider.Id, integration.ProviderId);
        Assert.False(integration.IsEnabled);
    }

    [Fact]
    public void TrialPodeSerAssociadoUmaUnicaVezAoTenant()
    {
        var trial = new TrialSubscription(Uuid7.New(), Uuid7.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));
        var tenantId = Uuid7.New();

        trial.AssociateWithTenant(tenantId);

        Assert.Equal(tenantId, trial.TenantId);
        Assert.Throws<InvalidOperationException>(() => trial.AssociateWithTenant(Uuid7.New()));
    }

    [Fact]
    public void Uuid7GeraIdentificadorPreenchido()
    {
        Assert.NotEqual(Guid.Empty, Uuid7.New());
    }
}
