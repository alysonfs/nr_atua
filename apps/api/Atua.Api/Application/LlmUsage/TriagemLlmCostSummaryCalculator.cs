using Atua.Api.Application.LlmUsage.Contracts;

namespace Atua.Api.Application.LlmUsage;

/// <summary>Totais de uso/custo de LLM de uma única triagem — mesmo cálculo de custo usado pelo relatório de consumo por tenant (ADR-048).</summary>
public sealed record TriagemLlmCostSummary(int Calls, long InputTokens, long OutputTokens, decimal EstimatedCostUsd)
{
    public static readonly TriagemLlmCostSummary Zero = new(0, 0, 0, 0m);
}

/// <summary>
/// Agrupa <see cref="TriagemLlmUsageByTriagemRow"/> por triagem e estima o
/// custo em USD reaproveitando <see cref="TriagemLlmPricingCatalog"/> — o
/// mesmo catálogo de preço já usado pelo relatório de custo por tenant
/// (ADR-048), para nunca duplicar a regra de precificação.
/// </summary>
public static class TriagemLlmCostSummaryCalculator
{
    public static IReadOnlyDictionary<Guid, TriagemLlmCostSummary> BuildByTriagemId(
        IReadOnlyList<TriagemLlmUsageByTriagemRow> rows, TriagemLlmPricingCatalog pricingCatalog) =>
        rows.GroupBy(row => row.TriagemId)
            .ToDictionary(group => group.Key, group => Build(group, pricingCatalog));

    public static TriagemLlmCostSummary Build(IEnumerable<TriagemLlmUsageByTriagemRow> rowsOfOneTriagem,
        TriagemLlmPricingCatalog pricingCatalog)
    {
        var rows = rowsOfOneTriagem.ToList();
        if (rows.Count == 0) return TriagemLlmCostSummary.Zero;

        var calls = rows.Sum(row => row.Calls);
        var inputTokens = rows.Sum(row => row.InputTokens);
        var outputTokens = rows.Sum(row => row.OutputTokens);
        var estimatedCost = rows.Sum(row => pricingCatalog.EstimateCostUsd(row.ModelId, row.InputTokens, row.OutputTokens));

        return new TriagemLlmCostSummary(calls, inputTokens, outputTokens, estimatedCost);
    }
}
