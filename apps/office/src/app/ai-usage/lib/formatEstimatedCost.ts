/**
 * Formatação de custo estimado em USD (`AiUsageTotals.estimatedCost` etc.).
 * Custos de uso de IA costumam ser frações de centavo por chamada — exibir
 * sempre com 2 casas decimais arredondaria para "$0.00" e esconderia a
 * informação. Usa 4 casas quando o valor é menor que 1 (e ainda > 0), e 2
 * casas no caso geral.
 */
export function formatEstimatedCost(value: number, currency: string, locale: string): string {
  const minimumFractionDigits = value > 0 && value < 1 ? 4 : 2
  const maximumFractionDigits = minimumFractionDigits

  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency,
      minimumFractionDigits,
      maximumFractionDigits,
    }).format(value)
  } catch {
    return `${currency} ${value.toFixed(minimumFractionDigits)}`
  }
}
