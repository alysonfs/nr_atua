using Atua.Api.Infrastructure.Persistence;

namespace Atua.Api.Tests;

/// <summary>
/// Testes estruturais (assertivas sobre o texto SQL) de
/// <see cref="TriagemChannelCommandPersistenceSchema.ClaimNextSql"/> (ADR-042).
///
/// Mesma limitação reconhecida em <c>TriagemOutboxClaimSqlTests</c>: o
/// provider EF Core InMemory não executa SQL relacional bruto
/// (<c>FromSqlRaw</c>), então este teste não prova o comportamento real de
/// <c>FOR UPDATE SKIP LOCKED</c>/concorrência/isolamento tenant+integração
/// contra um PostgreSQL de verdade — apenas que o texto do comando contém
/// as cláusulas e os parâmetros esperados.
/// </summary>
public class TriagemChannelCommandClaimSqlTests
{
    [Fact]
    public void ReivindicaComForUpdateSkipLocked()
    {
        Assert.Contains("FOR UPDATE SKIP LOCKED", TriagemChannelCommandPersistenceSchema.ClaimNextSql);
    }

    [Fact]
    public void FiltraPorTenantEIntegracaoDaCredencial()
    {
        var sql = TriagemChannelCommandPersistenceSchema.ClaimNextSql;

        // ADR-042: tenant/integração vêm exclusivamente dos claims da
        // credencial de serviço — nunca do corpo da requisição. O claim
        // atômico nunca reivindica um comando de outra tenant/integração.
        Assert.Contains("\"TenantId\" = @tenantId", sql);
        Assert.Contains("\"IntegrationId\" = @integrationId", sql);
    }

    [Fact]
    public void FiltraPendingOuClaimedComLeaseExpirada()
    {
        var sql = TriagemChannelCommandPersistenceSchema.ClaimNextSql;

        Assert.Contains("\"Status\" = 'Pending'", sql);
        Assert.Contains("\"Status\" = 'Claimed' AND \"ClaimExpiresAtUtc\" < @now", sql);
    }

    [Fact]
    public void MarcaClaimedComNovaLeaseAoReivindicar()
    {
        var sql = TriagemChannelCommandPersistenceSchema.ClaimNextSql;

        Assert.Contains("SET \"Status\" = 'Claimed'", sql);
        Assert.Contains("\"ClaimedAtUtc\" = @now", sql);
        Assert.Contains("\"ClaimExpiresAtUtc\" = @claimExpiresAtUtc", sql);
    }

    [Fact]
    public void LimitaAUmaLinhaPorChamada()
    {
        // Diferente do outbox (LIMIT @batchSize): o claim do canal sempre
        // reivindica exatamente um comando por chamada (ADR-042).
        Assert.Contains("LIMIT 1", TriagemChannelCommandPersistenceSchema.ClaimNextSql);
    }

    [Fact]
    public void NaoAlteraIdempotencyKeyNoClaim()
    {
        // ADR-042: lease expirada torna o comando reclamável sem alterar a
        // chave de idempotência.
        Assert.DoesNotContain("IdempotencyKey", TriagemChannelCommandPersistenceSchema.ClaimNextSql);
    }

    [Fact]
    public void NaoAlteraAttemptCountNoClaim()
    {
        // AttemptCount só muda em RecordTransientFailure (complete), nunca
        // no claim.
        Assert.DoesNotContain("AttemptCount", TriagemChannelCommandPersistenceSchema.ClaimNextSql);
    }

    [Fact]
    public void OperaSobreATabelaCorreta()
    {
        var sql = TriagemChannelCommandPersistenceSchema.ClaimNextSql;

        Assert.Contains($"FROM {TriagemChannelCommandPersistenceSchema.TableName}", sql);
        Assert.Contains($"UPDATE {TriagemChannelCommandPersistenceSchema.TableName}", sql);
    }

    [Fact]
    public void OrdenaPorCreatedAtUtcCrescente()
    {
        Assert.Contains("ORDER BY \"CreatedAtUtc\" ASC", TriagemChannelCommandPersistenceSchema.ClaimNextSql);
    }
}
