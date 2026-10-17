using Atua.Api.Application.Triagens;
using Atua.Api.Infrastructure.Persistence;

namespace Atua.Api.Tests;

/// <summary>
/// Testes estruturais (assertivas sobre o texto SQL) de
/// <see cref="TriagemOutboxPersistenceSchema.ClaimBatchSql"/> (ADR-041).
///
/// Limitação reconhecida: o provider EF Core InMemory não executa SQL
/// relacional bruto (<c>FromSqlRaw</c>), então este teste não prova o
/// comportamento real de <c>FOR UPDATE SKIP LOCKED</c>/concorrência contra
/// um PostgreSQL de verdade — apenas que o texto do comando contém as
/// cláusulas esperadas e os parâmetros corretos. Mesma limitação já
/// documentada em <c>TriagemRepositoryTests</c> para a ausência de
/// concorrência real do provider InMemory.
/// </summary>
public class TriagemOutboxClaimSqlTests
{
    [Fact]
    public void ReivindicaComForUpdateSkipLocked()
    {
        Assert.Contains("FOR UPDATE SKIP LOCKED", TriagemOutboxPersistenceSchema.ClaimBatchSql);
    }

    [Fact]
    public void FiltraPendingVencidoOuProcessingComLeaseExpirada()
    {
        var sql = TriagemOutboxPersistenceSchema.ClaimBatchSql;

        Assert.Contains("\"Status\" = 'Pending' AND \"NextAttemptAtUtc\" <= @now", sql);
        Assert.Contains("\"Status\" = 'Processing' AND \"ClaimExpiresAtUtc\" < @now", sql);
    }

    [Fact]
    public void MarcaProcessingComNovaLeaseAoReivindicar()
    {
        var sql = TriagemOutboxPersistenceSchema.ClaimBatchSql;

        Assert.Contains("SET \"Status\" = 'Processing'", sql);
        Assert.Contains("\"ClaimedAtUtc\" = @now", sql);
        Assert.Contains("\"ClaimExpiresAtUtc\" = @claimExpiresAtUtc", sql);
    }

    [Fact]
    public void LimitaOLoteAoParametroBatchSize()
    {
        Assert.Contains("LIMIT @batchSize", TriagemOutboxPersistenceSchema.ClaimBatchSql);
    }

    [Fact]
    public void NaoAlteraAttemptCountNoClaim()
    {
        // Requisito ADR-041: AttemptCount só é incrementado em falha de
        // processamento (TriagemTriggerOutbox.RecordFailure), nunca no claim.
        Assert.DoesNotContain("AttemptCount", TriagemOutboxPersistenceSchema.ClaimBatchSql);
    }

    [Fact]
    public void OperaSobreATabelaCorreta()
    {
        Assert.Contains($"FROM {TriagemOutboxPersistenceSchema.TableName}", TriagemOutboxPersistenceSchema.ClaimBatchSql);
        Assert.Contains($"UPDATE {TriagemOutboxPersistenceSchema.TableName}", TriagemOutboxPersistenceSchema.ClaimBatchSql);
    }

    [Fact]
    public void TravaTemporaria_ReivindicaSomenteOsPermitidasAntesDosFiltrosDeStatus()
    {
        var sql = TriagemOutboxPersistenceSchema.ClaimBatchSql;

        Assert.Contains("WHERE \"WorkOrderId\" = ANY(@allowedWorkOrderIds)", sql);
        Assert.Contains("AND ((\"Status\" = 'Pending'", sql);
        Assert.Contains("\"ClaimExpiresAtUtc\" < @now))", sql);
    }

    [Fact]
    public void TravaTemporaria_ListaContemSomenteAOsDeTeste()
    {
        Assert.Equal(
            [Guid.Parse("01a0f938-dabc-7ca6-b95c-6f0415aa466e")],
            TriagemTestSafeguard.AllowedWorkOrderIds);
    }
}
