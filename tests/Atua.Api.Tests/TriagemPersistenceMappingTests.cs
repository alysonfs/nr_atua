using System.Text.Json;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de mapeamento do agregado Triagem (ADR-040). Usam
/// <c>UseNpgsql</c> com uma connection string inválida apenas para
/// construção do modelo — mesma técnica de <see cref="PersistenceMappingTests"/>
/// — sem abrir conexão real com o banco.
/// </summary>
public class TriagemPersistenceMappingTests
{
    [Fact]
    public void TriagemPossuiIndiceUnicoDeIdempotenciaComNomeEstavel()
    {
        using var context = CreateContext();
        var triagem = context.Model.FindEntityType(typeof(Triagem))!;

        var index = triagem.GetIndexes().Single(item => item.Properties.Count == 3);

        Assert.True(index.IsUnique);
        Assert.Equal("uq_triagens_tenant_integration_work_order_provider_id", index.GetDatabaseName());
        Assert.Equal(
            [nameof(Triagem.TenantId), nameof(Triagem.IntegrationId), "WorkOrderProviderId"],
            index.Properties.Select(property => property.Name));
    }

    [Fact]
    public void TriagemPersisteWorkOrderProviderIdComoShadowPropertyObrigatoria()
    {
        using var context = CreateContext();
        var triagem = context.Model.FindEntityType(typeof(Triagem))!;

        var shadowProperty = triagem.FindProperty("WorkOrderProviderId")!;

        Assert.True(shadowProperty.IsShadowProperty());
        Assert.False(shadowProperty.IsNullable);
        Assert.Equal(256, shadowProperty.GetMaxLength());
    }

    [Fact]
    public void TriagemPersisteTodosOsCamposEssenciaisDoChecklistEContadores()
    {
        using var context = CreateContext();
        var triagem = context.Model.FindEntityType(typeof(Triagem))!;

        foreach (var propertyName in new[]
                 {
                     nameof(Triagem.Id), nameof(Triagem.TenantId), nameof(Triagem.IntegrationId),
                     nameof(Triagem.WorkOrderId), nameof(Triagem.Status), nameof(Triagem.CreatedAtUtc),
                     nameof(Triagem.UpdatedAtUtc), nameof(Triagem.DescricaoDefeito),
                     nameof(Triagem.NotaFiscalLegivel), nameof(Triagem.EtiquetaModelo),
                     nameof(Triagem.EtiquetaSerie), nameof(Triagem.MidiaDefeitoTipo),
                     nameof(Triagem.EnderecoCompleto), nameof(Triagem.PontoReferencia),
                     nameof(Triagem.Localizacao), nameof(Triagem.DesviosConsecutivos),
                 })
        {
            Assert.NotNull(triagem.FindProperty(propertyName));
        }

        // Contadores de tentativa inválida por item — campo privado, mapeado
        // pelo nome do backing field, não pela propriedade pública somente
        // leitura.
        var tentativas = triagem.FindProperty("_tentativasInvalidasPorItem");
        Assert.NotNull(tentativas);
        Assert.Equal("TentativasInvalidasPorItem", tentativas!.GetColumnName());
        Assert.Equal("jsonb", tentativas.GetColumnType());
    }

    [Fact]
    public void TriagemNaoPersisteNenhumBinario()
    {
        using var context = CreateContext();
        var triagem = context.Model.FindEntityType(typeof(Triagem))!;

        foreach (var property in triagem.GetProperties())
        {
            Assert.NotEqual(typeof(byte[]), property.ClrType);
        }
    }

    [Fact]
    public void TriagemPossuiIndicesDeApoioParaConsultaPorTenantEIntegracao()
    {
        using var context = CreateContext();
        var triagem = context.Model.FindEntityType(typeof(Triagem))!;

        Assert.Contains(triagem.GetIndexes(),
            index => index.Properties.Count == 1 && index.Properties[0].Name == nameof(Triagem.TenantId));
        Assert.Contains(triagem.GetIndexes(),
            index => index.Properties.Count == 1 && index.Properties[0].Name == nameof(Triagem.IntegrationId));
    }

    [Fact]
    public void TentativasInvalidasPorItem_JsonVazio_ReidratraDicionarioVazio()
    {
        using var context = CreateContext();
        var triagem = context.Model.FindEntityType(typeof(Triagem))!;
        var converter = triagem.FindProperty("_tentativasInvalidasPorItem")!.GetValueConverter()!;

        var resultado = converter.ConvertFromProvider("{}");

        var dicionario = Assert.IsType<Dictionary<ETriagemChecklistItem, int>>(resultado);
        Assert.Empty(dicionario);
    }

    [Fact]
    public void TentativasInvalidasPorItem_JsonNulo_FalhaExplicitamenteEmVezDeViraVazio()
    {
        using var context = CreateContext();
        var triagem = context.Model.FindEntityType(typeof(Triagem))!;
        var converter = triagem.FindProperty("_tentativasInvalidasPorItem")!.GetValueConverter()!;

        var exception = Assert.Throws<JsonException>(() => converter.ConvertFromProvider("null"));

        Assert.Contains("TentativasInvalidasPorItem", exception.Message);
    }

    private static AtuaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseNpgsql("Host=localhost;Database=atua;Username=atua")
            .Options;

        return new AtuaDbContext(options);
    }
}
