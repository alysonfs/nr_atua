using Atua.Collector.Consumer;

namespace Atua.Collector.Tests;

/// <summary>
/// Testes de <see cref="TriagemTriggerEligibility"/> (ADR-041) — comparação
/// exata e ordinal (case-sensitive) com o status <c>assigned</c>.
/// </summary>
public class TriagemTriggerEligibilityTests
{
    [Fact]
    public void Assigned_EhElegivel()
    {
        Assert.True(TriagemTriggerEligibility.IsEligible("assigned"));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("accepted")]
    [InlineData("Assigned")]
    [InlineData("ASSIGNED")]
    [InlineData("assigned ")]
    [InlineData(" assigned")]
    [InlineData("")]
    [InlineData("finished")]
    public void OutrosStatusOuVariacoesDeCaixa_NaoSaoElegiveis(string status)
    {
        Assert.False(TriagemTriggerEligibility.IsEligible(status));
    }
}
