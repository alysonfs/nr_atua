using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de mapeamento de <see cref="TriagemLlmUsage"/> (ADR-048) — mesmo
/// padrão de <see cref="TriagemPersistenceMappingTests"/>: valida o modelo
/// EF Core sem abrir conexão real com o Postgres.
/// </summary>
public class TriagemLlmUsagePersistenceMappingTests
{
    [Fact]
    public void MapeiaParaATabelaTriagemLlmUsageComChavePrimariaSemGeracaoDeValor()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(TriagemLlmUsage))!;

        Assert.Equal(TriagemLlmUsagePersistenceSchema.TableName, entity.GetTableName());
        Assert.Equal(ValueGenerated.Never, entity.FindProperty(nameof(TriagemLlmUsage.Id))!.ValueGenerated);
    }

    [Fact]
    public void PossuiIndicesParaConsultaPorTenantEPorTriagem()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(TriagemLlmUsage))!;

        var tenantIndex = entity.GetIndexes().Single(index =>
            index.Properties.Select(p => p.Name).SequenceEqual(
                [nameof(TriagemLlmUsage.TenantId), nameof(TriagemLlmUsage.CreatedAtUtc)]));
        Assert.False(tenantIndex.IsUnique);

        var triagemIndex = entity.GetIndexes().Single(index =>
            index.Properties.Select(p => p.Name).SequenceEqual([nameof(TriagemLlmUsage.TriagemId)]));
        Assert.False(triagemIndex.IsUnique);
    }

    [Fact]
    public void PossuiChavesEstrangeirasComDeleteBehaviorRestrict()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(TriagemLlmUsage))!;

        var foreignKeys = entity.GetForeignKeys().ToList();
        Assert.Equal(2, foreignKeys.Count);
        Assert.All(foreignKeys, fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Contains(foreignKeys, fk => fk.PrincipalEntityType.ClrType == typeof(Domain.Tenants.Tenant));
        Assert.Contains(foreignKeys, fk => fk.PrincipalEntityType.ClrType == typeof(Domain.Triagens.Triagem));
    }

    [Fact]
    public void OutcomeEPersistidoComoStringComTamanhoMaximo()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(TriagemLlmUsage))!;
        var outcome = entity.FindProperty(nameof(TriagemLlmUsage.Outcome))!;

        Assert.Equal(typeof(string), outcome.GetProviderClrType() ?? outcome.ClrType);
        Assert.Equal(32, outcome.GetMaxLength());
    }

    private static AtuaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseNpgsql("Host=localhost;Database=atua;Username=atua")
            .Options;

        return new AtuaDbContext(options);
    }
}
