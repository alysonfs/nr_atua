using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de mapeamento de <see cref="TriagemTriggerOutbox"/> (ADR-041).
/// Usam <c>UseNpgsql</c> com uma connection string inválida apenas para
/// construção do modelo — mesma técnica de <see cref="TriagemPersistenceMappingTests"/>
/// — sem abrir conexão real com o banco.
/// </summary>
public class TriagemTriggerOutboxPersistenceMappingTests
{
    [Fact]
    public void MapeiaParaATabelaEsperada()
    {
        using var context = CreateContext();
        var outbox = context.Model.FindEntityType(typeof(TriagemTriggerOutbox))!;

        Assert.Equal("triagem_trigger_outbox", outbox.GetTableName());
    }

    [Fact]
    public void PossuiIndiceUnicoDeIdempotenciaTenantIntegrationWorkOrderProviderId()
    {
        using var context = CreateContext();
        var outbox = context.Model.FindEntityType(typeof(TriagemTriggerOutbox))!;

        var index = outbox.GetIndexes().Single(item => item.Properties.Count == 3);

        Assert.True(index.IsUnique);
        Assert.Equal("uq_triagem_trigger_outbox_tenant_integration_workorder", index.GetDatabaseName());
        Assert.Equal(
            [nameof(TriagemTriggerOutbox.TenantId), nameof(TriagemTriggerOutbox.IntegrationId),
                nameof(TriagemTriggerOutbox.WorkOrderProviderId)],
            index.Properties.Select(property => property.Name));
    }

    [Fact]
    public void PossuiIndiceDeApoioAoClaimPorStatusEProximaTentativa()
    {
        using var context = CreateContext();
        var outbox = context.Model.FindEntityType(typeof(TriagemTriggerOutbox))!;

        var index = outbox.GetIndexes().Single(item =>
            item.GetDatabaseName() == "ix_triagem_trigger_outbox_claim_due");

        Assert.Equal(
            [nameof(TriagemTriggerOutbox.Status), nameof(TriagemTriggerOutbox.NextAttemptAtUtc)],
            index.Properties.Select(property => property.Name));
    }

    [Fact]
    public void PossuiIndiceParcialDeApoioAoClaimPorLeaseExpirada()
    {
        using var context = CreateContext();
        var outbox = context.Model.FindEntityType(typeof(TriagemTriggerOutbox))!;

        var index = outbox.GetIndexes().Single(item =>
            item.GetDatabaseName() == "ix_triagem_trigger_outbox_claim_lease");

        Assert.Equal([nameof(TriagemTriggerOutbox.ClaimExpiresAtUtc)],
            index.Properties.Select(property => property.Name));
        Assert.Equal("\"Status\" = 'Processing'", index.GetFilter());
    }

    [Fact]
    public void PossuiForeignKeysParaIntegrationEWorkOrderComDeleteRestrict()
    {
        using var context = CreateContext();
        var outbox = context.Model.FindEntityType(typeof(TriagemTriggerOutbox))!;

        var foreignKeys = outbox.GetForeignKeys().ToList();

        Assert.Equal(2, foreignKeys.Count);
        Assert.All(foreignKeys, fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Contains(foreignKeys, fk => fk.PrincipalEntityType.ClrType == typeof(Atua.Api.Domain.Integrations.Integration));
        Assert.Contains(foreignKeys, fk => fk.PrincipalEntityType.ClrType == typeof(Atua.Api.Domain.WorkOrders.WorkOrder));
    }

    [Fact]
    public void ClaimedAtUtcEhTokenDeConcorrenciaOtimista()
    {
        using var context = CreateContext();
        var outbox = context.Model.FindEntityType(typeof(TriagemTriggerOutbox))!;

        var claimedAtUtc = outbox.FindProperty(nameof(TriagemTriggerOutbox.ClaimedAtUtc))!;

        Assert.True(claimedAtUtc.IsConcurrencyToken);
    }

    [Fact]
    public void NaoPersisteNenhumBinario()
    {
        using var context = CreateContext();
        var outbox = context.Model.FindEntityType(typeof(TriagemTriggerOutbox))!;

        foreach (var property in outbox.GetProperties())
        {
            Assert.NotEqual(typeof(byte[]), property.ClrType);
        }
    }

    [Fact]
    public void StatusELastErrorCodeSaoPersistidosComoString()
    {
        using var context = CreateContext();
        var outbox = context.Model.FindEntityType(typeof(TriagemTriggerOutbox))!;

        var status = outbox.FindProperty(nameof(TriagemTriggerOutbox.Status))!;
        var lastErrorCode = outbox.FindProperty(nameof(TriagemTriggerOutbox.LastErrorCode))!;

        Assert.Equal(typeof(string), status.GetProviderClrType() ?? status.ClrType);
        Assert.Equal(typeof(string), lastErrorCode.GetProviderClrType() ?? lastErrorCode.ClrType);
    }

    private static AtuaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseNpgsql("Host=localhost;Database=atua;Username=atua")
            .Options;

        return new AtuaDbContext(options);
    }
}
