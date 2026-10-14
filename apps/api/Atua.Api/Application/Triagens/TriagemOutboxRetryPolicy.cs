namespace Atua.Api.Application.Triagens;

/// <summary>
/// Backoff exponencial determinístico (sem jitter) para nova tentativa de
/// processamento de <see cref="Domain.Triagens.TriagemTriggerOutbox"/>
/// (ADR-041, requisito "backoff exponencial limitado e determinístico").
/// </summary>
public static class TriagemOutboxRetryPolicy
{
    /// <summary>
    /// Calcula o instante da próxima tentativa a partir de
    /// <paramref name="attemptCountAfterFailure"/> (contagem já incrementada
    /// pela falha atual — 1 na primeira falha).
    /// </summary>
    public static DateTimeOffset ComputeNextAttemptAtUtc(DateTimeOffset now, int attemptCountAfterFailure,
        int baseRetrySeconds, int maxRetrySeconds) =>
        now.AddSeconds(ComputeDelaySeconds(attemptCountAfterFailure, baseRetrySeconds, maxRetrySeconds));

    /// <summary>
    /// <c>min(baseRetrySeconds * 2^(attemptCountAfterFailure - 1), maxRetrySeconds)</c>.
    /// Internal — exposto a <c>Atua.Api.Tests</c> via <c>InternalsVisibleTo</c>
    /// para testar a progressão do backoff isoladamente.
    /// </summary>
    internal static int ComputeDelaySeconds(int attemptCountAfterFailure, int baseRetrySeconds, int maxRetrySeconds)
    {
        if (attemptCountAfterFailure < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptCountAfterFailure),
                attemptCountAfterFailure, "Deve ser >= 1 (contagem já incrementada pela falha atual).");
        }

        var exponent = attemptCountAfterFailure - 1;

        // Teto do expoente evita overflow de long antes do Math.Min já
        // cortar pelo teto configurado — acima disso o valor já teria
        // estourado maxRetrySeconds de qualquer forma.
        const int maxExponent = 32;
        if (exponent > maxExponent)
        {
            return maxRetrySeconds;
        }

        var delaySeconds = (long)baseRetrySeconds * (1L << exponent);
        return (int)Math.Min(delaySeconds, maxRetrySeconds);
    }
}
