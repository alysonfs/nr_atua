namespace Atua.Api.Domain.Triagens;

/// <summary>
/// Calcula o indicador de garantia na leitura (nunca persistido — ver
/// <see cref="EWarrantyEstimateStatus"/>), a partir da data de emissão da
/// nota fiscal e do prazo padrão configurado (decisão do PO: estimativa
/// provisória e global; no futuro, prazo por produto e por empresa). Lógica
/// pura e isolada para ser reaproveitada por todos os pontos de leitura
/// (detalhe do pool de triagem e detalhe da OS) sem duplicação.
/// </summary>
public static class WarrantyEstimateCalculator
{
    public static WarrantyEstimate Calculate(DateTimeOffset? dataEmissaoNotaFiscal, int defaultMonths,
        DateTimeOffset now)
    {
        if (dataEmissaoNotaFiscal is null)
        {
            return new WarrantyEstimate(EWarrantyEstimateStatus.Indeterminado, null, defaultMonths);
        }

        var limite = dataEmissaoNotaFiscal.Value.AddMonths(defaultMonths);
        var status = now <= limite
            ? EWarrantyEstimateStatus.ProvavelmenteNaGarantia
            : EWarrantyEstimateStatus.ProvavelmenteForaDaGarantia;

        return new WarrantyEstimate(status, dataEmissaoNotaFiscal, defaultMonths);
    }
}

/// <param name="Status">Indicador calculado — nunca persistido (decisão do PO).</param>
/// <param name="BaseDate">Data de emissão da nota fiscal usada no cálculo; <c>null</c> quando <see cref="EWarrantyEstimateStatus.Indeterminado"/>.</param>
/// <param name="DefaultMonths">Prazo padrão (em meses) efetivamente usado — configurável em <c>TriagemLlm:GarantiaPadraoMeses</c>.</param>
public sealed record WarrantyEstimate(EWarrantyEstimateStatus Status, DateTimeOffset? BaseDate, int DefaultMonths);
