using Atua.Api.Application.Tenants;

namespace Atua.Api.Tests;

public class CnpjValidatorTests
{
    [Theory]
    [InlineData("11.122.233/0001-83")]
    [InlineData("11122233000183")]
    public void AceitaCnpjValidoComOuSemMascara(string cnpj)
    {
        Assert.True(CnpjValidator.IsValid(cnpj));
        Assert.Equal("11122233000183", CnpjValidator.Normalize(cnpj));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("11111111111111")]
    [InlineData("11122233000199")]
    [InlineData("abc")]
    public void RejeitaCnpjInvalido(string? cnpj)
    {
        Assert.False(CnpjValidator.IsValid(cnpj));
        Assert.Null(CnpjValidator.Normalize(cnpj));
    }
}
