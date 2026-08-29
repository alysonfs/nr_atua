using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Atua.Api.Tests;

public class PersistenceMappingTests
{
    [Fact]
    public void UserPossuiIndiceUnicoParaEmail()
    {
        using var context = CreateContext();
        var user = context.Model.FindEntityType(typeof(User))!;

        var emailIndex = user.GetIndexes().Single(index =>
            index.Properties.Single().Name == nameof(User.Email));

        Assert.True(emailIndex.IsUnique);
        Assert.Equal(ValueGenerated.Never, user.FindProperty(nameof(User.Id))!.ValueGenerated);
    }

    [Fact]
    public void TenantPossuiIndiceUnicoParaCnpj()
    {
        using var context = CreateContext();
        var tenant = context.Model.FindEntityType(typeof(Tenant))!;

        var cnpjIndex = tenant.GetIndexes().Single(index =>
            index.Properties.Single().Name == nameof(Tenant.Cnpj));

        Assert.True(cnpjIndex.IsUnique);
    }

    [Fact]
    public void MembershipPossuiChaveCompostaDeTenantEUsuario()
    {
        using var context = CreateContext();
        var membership = context.Model.FindEntityType(typeof(TenantMembership))!;
        var key = membership.FindPrimaryKey()!;

        Assert.Collection(key.Properties,
            property => Assert.Equal(nameof(TenantMembership.TenantId), property.Name),
            property => Assert.Equal(nameof(TenantMembership.UserId), property.Name));
    }

    private static AtuaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseNpgsql("Host=localhost;Database=atua;Username=atua")
            .Options;

        return new AtuaDbContext(options);
    }
}