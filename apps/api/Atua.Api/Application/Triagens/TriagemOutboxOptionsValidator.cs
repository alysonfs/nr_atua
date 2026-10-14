using Microsoft.Extensions.Options;

namespace Atua.Api.Application.Triagens;

/// <summary>
/// Regra de validação entre campos de <see cref="TriagemOutboxOptions"/> que
/// <see cref="System.ComponentModel.DataAnnotations"/> não expressa sozinho
/// (ADR-041): o teto do backoff nunca pode ser menor que o atraso base,
/// senão o backoff "exponencial" na prática encolheria a cada tentativa.
/// </summary>
public sealed class TriagemOutboxOptionsValidator : IValidateOptions<TriagemOutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, TriagemOutboxOptions options)
    {
        if (options.MaxRetrySeconds < options.BaseRetrySeconds)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TriagemOutboxOptions.MaxRetrySeconds)} ({options.MaxRetrySeconds}) não pode ser menor " +
                $"que {nameof(TriagemOutboxOptions.BaseRetrySeconds)} ({options.BaseRetrySeconds}).");
        }

        return ValidateOptionsResult.Success;
    }
}
