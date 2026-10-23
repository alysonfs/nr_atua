namespace Atua.Api.Application.LlmUsage;

/// <summary>Preço de um modelo, em USD por milhão de tokens (seção <c>TriagemLlm:Pricing</c>).</summary>
public sealed record TriagemLlmModelPricing(string ModelId, decimal InputPerMillion, decimal OutputPerMillion);

/// <summary>
/// Preço por modelo usado para estimar o custo de uso de LLM por tenant
/// (ADR-048). Configurável pela seção <c>TriagemLlm:Pricing</c>
/// (appsettings) — uma lista de <see cref="TriagemLlmModelPricing"/> que
/// sobrescreve/complementa o default embutido para
/// <c>global.anthropic.claude-haiku-4-5-20251001-v1:0</c> (decisão do PO em
/// ADR-048: USD 1,00 de entrada e USD 5,00 de saída por milhão de tokens).
/// Modelo sem preço configurado nem default conhecido tem custo estimado
/// zero — nunca quebra o relatório de consumo.
/// </summary>
public sealed class TriagemLlmPricingCatalog
{
    public const string SectionName = "TriagemLlm:Pricing";

    private static readonly IReadOnlyDictionary<string, TriagemLlmModelPricing> DefaultPricing =
        new Dictionary<string, TriagemLlmModelPricing>(StringComparer.Ordinal)
        {
            ["global.anthropic.claude-haiku-4-5-20251001-v1:0"] =
                new TriagemLlmModelPricing("global.anthropic.claude-haiku-4-5-20251001-v1:0", 1.00m, 5.00m),
        };

    private readonly IReadOnlyDictionary<string, TriagemLlmModelPricing> _pricingByModelId;

    public TriagemLlmPricingCatalog(IEnumerable<TriagemLlmModelPricing>? configuredPricing = null)
    {
        var merged = new Dictionary<string, TriagemLlmModelPricing>(DefaultPricing, StringComparer.Ordinal);
        if (configuredPricing is not null)
        {
            foreach (var entry in configuredPricing)
            {
                if (string.IsNullOrWhiteSpace(entry.ModelId)) continue;
                merged[entry.ModelId] = entry; // configuração sobrescreve o default embutido
            }
        }

        _pricingByModelId = merged;
    }

    /// <summary>Custo estimado em USD para a quantidade de tokens informada. Modelo desconhecido retorna 0.</summary>
    public decimal EstimateCostUsd(string modelId, long inputTokens, long outputTokens)
    {
        if (!_pricingByModelId.TryGetValue(modelId, out var pricing)) return 0m;

        var inputCost = inputTokens / 1_000_000m * pricing.InputPerMillion;
        var outputCost = outputTokens / 1_000_000m * pricing.OutputPerMillion;
        return inputCost + outputCost;
    }
}
