using Microsoft.Extensions.Options;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>
/// Regra de validação entre campos de <see cref="TriagemLlmOptions"/> que
/// <see cref="System.ComponentModel.DataAnnotations"/> não expressa sozinho
/// (ADR-048): <see cref="TriagemLlmOptions.ModelId"/> só é obrigatório
/// quando o adaptador Bedrock está ligado — com o fake (<c>Enabled=false</c>),
/// nenhuma chamada usa o modelo, então não há por que exigi-lo.
/// </summary>
public sealed class TriagemLlmOptionsValidator : IValidateOptions<TriagemLlmOptions>
{
    public ValidateOptionsResult Validate(string? name, TriagemLlmOptions options)
    {
        if (options.Enabled && string.IsNullOrWhiteSpace(options.ModelId))
        {
            return ValidateOptionsResult.Fail(
                $"{TriagemLlmOptions.SectionName}:ModelId é obrigatório quando " +
                $"{TriagemLlmOptions.SectionName}:Enabled é true.");
        }

        return ValidateOptionsResult.Success;
    }
}
