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

    [Fact]
    public void TrialPossuiIndiceUnicoParaUsuario()
    {
        using var context = CreateContext();
        var trial = context.Model.FindEntityType(typeof(Domain.Billing.TrialSubscription))!;

        var userIndex = trial.GetIndexes().Single(index =>
            index.Properties.Single().Name == nameof(Domain.Billing.TrialSubscription.UserId));

        Assert.True(userIndex.IsUnique);
    }

    [Fact]
    public void AuthSessionPersisteOverrideOpcionalDeFusoHorario()
    {
        using var context = CreateContext();
        var session = context.Model.FindEntityType(typeof(Domain.Identity.AuthSession))!;

        Assert.True(session.FindProperty(nameof(Domain.Identity.AuthSession.TimeZoneOverrideId))!
            .IsNullable);
    }

    [Fact]
    public void ComandoImediatoPossuiIndiceUnicoParcialParaPending()
    {
        // ADR-020/RN-008.4: no máximo um comando Pending por integração.
        using var context = CreateContext();
        var command = context.Model.FindEntityType(
            typeof(Domain.Integrations.CollectorControl.ImmediateCollectionCommand))!;

        var pendingIndex = command.GetIndexes().Single(index =>
            index.GetDatabaseName() == "IX_immediate_collection_commands_IntegrationId_Pending");

        Assert.True(pendingIndex.IsUnique);
        Assert.Equal("\"Status\" = 'Pending'", pendingIndex.GetFilter());
    }

    [Fact]
    public void AtivacaoDoColetorPossuiIndiceUnicoPorIntegracao()
    {
        using var context = CreateContext();
        var activation = context.Model.FindEntityType(
            typeof(Domain.Integrations.CollectorControl.CollectorActivation))!;

        var index = activation.GetIndexes().Single(item =>
            item.Properties.Count == 1 && item.Properties.Single().Name == "IntegrationId");

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void IdempotenciaDoColetorPossuiIndiceUnicoComposto()
    {
        using var context = CreateContext();
        var record = context.Model.FindEntityType(
            typeof(Domain.Integrations.CollectorControl.CollectorControlIdempotency))!;

        var index = record.GetIndexes().Single(item => item.Properties.Count == 3);

        Assert.True(index.IsUnique);
        Assert.Equal(["IntegrationId", "Operation", "IdempotencyKey"],
            index.Properties.Select(property => property.Name));
    }

    private static AtuaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseNpgsql("Host=localhost;Database=atua;Username=atua")
            .Options;

        return new AtuaDbContext(options);
    }
}