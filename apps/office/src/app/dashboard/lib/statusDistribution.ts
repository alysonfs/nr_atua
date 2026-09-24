import type { WorkOrderStatusDistributionItem } from '../hooks/useWorkOrderMetricsSummary'

/** Chave sentinela para o item agregado "Outros" (não é um status real do provedor). */
export const OTHER_STATUS_KEY = '__other__'

/**
 * Reduz a distribuição de status aos `topN` mais frequentes, agrupando o
 * restante em um único item "Outros". Evita poluição visual (muitas cores/
 * categorias) em gráficos de comparação (Cotgreave: máx. 5-7 cores).
 */
export function aggregateTopStatuses(
  distribution: WorkOrderStatusDistributionItem[],
  topN = 5,
): WorkOrderStatusDistributionItem[] {
  const sorted = [...distribution].sort((a, b) => b.total - a.total)
  if (sorted.length <= topN) return sorted

  const top = sorted.slice(0, topN)
  const othersTotal = sorted.slice(topN).reduce((sum, item) => sum + item.total, 0)

  return othersTotal > 0 ? [...top, { status: OTHER_STATUS_KEY, total: othersTotal }] : top
}
