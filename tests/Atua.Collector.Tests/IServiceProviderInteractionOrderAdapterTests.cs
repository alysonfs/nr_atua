using Atua.Collector.Consumer;
using MongoDB.Bson;

namespace Atua.Collector.Tests;

/// <summary>
/// Testes unitários de <see cref="IServiceProviderInteractionOrderAdapter"/>, focados no
/// bug relatado em produção: dados de contato do consumidor (nome/CPF/e-mail/telefone/
/// endereço) permaneciam sempre mascarados ("S****S", "5584****51" etc.) na página de
/// detalhes da OS, mesmo após o enriquecimento — o adapter nunca lia o sub-documento
/// correto do payload não mascarado do detalhe (<c>orderDetail.customerInfo</c>, devolvido
/// por <c>queryWoExecutionDetail</c>), sempre extraindo dos campos de nível raiz (mascarados
/// na listagem).
/// </summary>
public class IServiceProviderInteractionOrderAdapterTests
{
    private readonly IServiceProviderInteractionOrderAdapter _adapter = new();

    private static BsonDocument MaskedListOrder(BsonDocument? customerInfo = null)
    {
        var order = new BsonDocument
        {
            ["workOrderId"] = "101570622",
            ["woStatus"] = "assigned",
            ["name"] = "S****S",
            ["cpf"] = BsonNull.Value,
            ["email"] = "su****@hotmail.com",
            ["phoneNumber1"] = "5584****51",
            ["phoneCountryCode1"] = BsonNull.Value,
            ["contactName"] = "S****S",
            ["address"] = "RU****il",
        };

        if (customerInfo is not null)
        {
            order["orderDetail"] = new BsonDocument { ["customerInfo"] = customerInfo };
        }

        return order;
    }

    [Fact]
    public void SemOrderDetail_UsaCamposMascaradosDaListagem()
    {
        var order = MaskedListOrder();

        var result = _adapter.ExtractOrders([order]);

        var data = Assert.Single(result);
        Assert.Equal("S****S", data.CustomerName);
        Assert.Equal("su****@hotmail.com", data.ContactEmail);
        Assert.Equal("5584****51", data.ContactPhone);
        Assert.Equal("RU****il", data.Address);
    }

    [Fact]
    public void ComOrderDetail_PreferoDadosNaoMascaradosDoDetalhe()
    {
        var customerInfo = new BsonDocument
        {
            ["name"] = "Suzana Silva",
            ["cpf"] = "12345678900",
            ["email"] = "suzana.silva@hotmail.com",
            ["phoneNumber1"] = "84991234451",
            ["phoneCountryCode1"] = "55",
            ["contactName"] = "Suzana Silva",
            ["address"] = "Rua das Flores, 123",
        };
        var order = MaskedListOrder(customerInfo);

        var result = _adapter.ExtractOrders([order]);

        var data = Assert.Single(result);
        Assert.Equal("Suzana Silva", data.CustomerName);
        Assert.Equal("12345678900", data.CustomerCpf);
        Assert.Equal("suzana.silva@hotmail.com", data.ContactEmail);
        Assert.Equal("5584991234451", data.ContactPhone);
        Assert.Equal("Suzana Silva", data.ContactName);
        Assert.Equal("Rua das Flores, 123", data.Address);
    }

    [Fact]
    public void ComOrderDetailParcial_CamposAusentesCaemParaOsMascaradosDaListagem()
    {
        // orderDetail.customerInfo existe mas só trouxe o telefone (ex.: payload de detalhe
        // incompleto) — os demais campos devem continuar vindo do nível raiz, não devem
        // virar null.
        var customerInfo = new BsonDocument
        {
            ["phoneNumber1"] = "84991234451",
        };
        var order = MaskedListOrder(customerInfo);

        var result = _adapter.ExtractOrders([order]);

        var data = Assert.Single(result);
        Assert.Equal("84991234451", data.ContactPhone);
        Assert.Equal("S****S", data.CustomerName);
        Assert.Equal("su****@hotmail.com", data.ContactEmail);
    }

    [Fact]
    public void ComPendingReasonPreenchido_UsaPendingReason()
    {
        var order = MaskedListOrder();
        order["pendingReason"] = "Peça - pendência de entrega";
        order["closeDescription"] = "Parts - delivery pending";

        var result = _adapter.ExtractOrders([order]);

        Assert.Equal("Peça - pendência de entrega", Assert.Single(result).SubStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SemPendingReason_CaiParaCloseDescription(string? pendingReason)
    {
        // Confirmado em produção (2026-09-28): pendingReason nunca vem preenchido para este
        // tenant (bug/instabilidade do iService); o dado equivalente é observado em
        // closeDescription.
        var order = MaskedListOrder();
        order["pendingReason"] = pendingReason is null ? BsonNull.Value : pendingReason;
        order["closeDescription"] = "Technician - visit confirmed";

        var result = _adapter.ExtractOrders([order]);

        Assert.Equal("Technician - visit confirmed", Assert.Single(result).SubStatus);
    }

    [Fact]
    public void SemPendingReasonNemCloseDescription_SubStatusFicaNulo()
    {
        var order = MaskedListOrder();

        var result = _adapter.ExtractOrders([order]);

        Assert.Null(Assert.Single(result).SubStatus);
    }
}
