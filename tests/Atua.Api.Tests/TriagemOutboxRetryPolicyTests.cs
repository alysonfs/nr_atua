using Atua.Api.Application.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Testes do backoff exponencial determinístico (sem jitter) de
/// <see cref="TriagemOutboxRetryPolicy"/> (ADR-041).
/// </summary>
public class TriagemOutboxRetryPolicyTests
{
    [Theory]
    [InlineData(1, 5, 300, 5)]
    [InlineData(2, 5, 300, 10)]
    [InlineData(3, 5, 300, 20)]
    [InlineData(4, 5, 300, 40)]
    [InlineData(5, 5, 300, 80)]
    [InlineData(6, 5, 300, 160)]
    public void CresceExponencialmenteAntesDoTeto(int attemptCount, int baseSeconds, int maxSeconds, int expected)
    {
        var delay = TriagemOutboxRetryPolicy.ComputeDelaySeconds(attemptCount, baseSeconds, maxSeconds);

        Assert.Equal(expected, delay);
    }

    [Fact]
    public void ÉLimitadoPeloTeto()
    {
        var delay = TriagemOutboxRetryPolicy.ComputeDelaySeconds(10, 5, 300);

        Assert.Equal(300, delay);
    }

    [Fact]
    public void NaoEstouraComExpoenteMuitoGrande()
    {
        var delay = TriagemOutboxRetryPolicy.ComputeDelaySeconds(int.MaxValue / 2, 5, 300);

        Assert.Equal(300, delay);
    }

    [Fact]
    public void RejeitaAttemptCountMenorQueUm()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TriagemOutboxRetryPolicy.ComputeDelaySeconds(0, 5, 300));
    }

    [Fact]
    public void EhDeterministicoParaOsMesmosParametros()
    {
        var first = TriagemOutboxRetryPolicy.ComputeDelaySeconds(3, 5, 300);
        var second = TriagemOutboxRetryPolicy.ComputeDelaySeconds(3, 5, 300);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeNextAttemptAtUtcSomaODelayAoInstanteFornecido()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var nextAttempt = TriagemOutboxRetryPolicy.ComputeNextAttemptAtUtc(now, 2, 5, 300);

        Assert.Equal(now.AddSeconds(10), nextAttempt);
    }
}
