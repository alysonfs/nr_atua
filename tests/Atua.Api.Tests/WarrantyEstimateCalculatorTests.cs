using Atua.Api.Domain.Triagens;
using Xunit;

namespace Atua.Api.Tests;

public class WarrantyEstimateCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SemDataEmissao_RetornaIndeterminado()
    {
        var result = WarrantyEstimateCalculator.Calculate(null, 12, Now);

        Assert.Equal(EWarrantyEstimateStatus.Indeterminado, result.Status);
        Assert.Null(result.BaseDate);
        Assert.Equal(12, result.DefaultMonths);
    }

    [Fact]
    public void DentroDoPrazo_RetornaProvavelmenteNaGarantia()
    {
        var result = WarrantyEstimateCalculator.Calculate(Now.AddMonths(-11), 12, Now);

        Assert.Equal(EWarrantyEstimateStatus.ProvavelmenteNaGarantia, result.Status);
    }

    [Fact]
    public void NoLimiteExato_RetornaProvavelmenteNaGarantia()
    {
        var result = WarrantyEstimateCalculator.Calculate(Now.AddMonths(-12), 12, Now);

        Assert.Equal(EWarrantyEstimateStatus.ProvavelmenteNaGarantia, result.Status);
    }

    [Fact]
    public void AposPrazo_RetornaProvavelmenteForaDaGarantia()
    {
        var result = WarrantyEstimateCalculator.Calculate(Now.AddMonths(-13), 12, Now);

        Assert.Equal(EWarrantyEstimateStatus.ProvavelmenteForaDaGarantia, result.Status);
    }
}
