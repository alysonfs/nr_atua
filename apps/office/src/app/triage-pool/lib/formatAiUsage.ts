import type { TFunction } from 'i18next'
import type { TriagemLlmUsage } from '../../../shared/types/triagePool'

/**
 * Formata o custo de IA em USD. Custos de triagem costumam ser frações de
 * centavo — usa 4 casas decimais quando o valor é menor que 1 (e maior que
 * 0) para não esconder a informação como "US$ 0,00" (mesmo critério de
 * `app/ai-usage/lib/formatEstimatedCost.ts`).
 */
export function formatAiUsageCost(costUsd: number, locale: string): string {
  const minimumFractionDigits = costUsd > 0 && costUsd < 1 ? 4 : 2

  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency: 'USD',
      minimumFractionDigits,
      maximumFractionDigits: minimumFractionDigits,
    }).format(costUsd)
  } catch {
    return `US$ ${costUsd.toFixed(minimumFractionDigits)}`
  }
}

/**
 * Rótulo curto do custo de IA para a coluna da lista do pool (C1): apenas o
 * custo em USD, ou "—" quando o campo ainda não é publicado pelo backend.
 */
export function formatAiUsageListLabel(llmUsage: TriagemLlmUsage | null | undefined, locale: string): string {
  if (llmUsage?.estimatedCostUsd === undefined || llmUsage.estimatedCostUsd === null) return '—'
  return formatAiUsageCost(llmUsage.estimatedCostUsd, locale)
}

/**
 * Rótulo detalhado do custo de IA para o detalhe da triagem (C2) e o card
 * de triagem da OS: custo + tokens de entrada/saída + número de chamadas,
 * omitindo partes ausentes. "—" quando nenhum dado estiver disponível.
 */
export function formatAiUsageDetailLabel(
  t: TFunction,
  llmUsage: TriagemLlmUsage | null | undefined,
  locale: string,
): string {
  if (!llmUsage || llmUsage.estimatedCostUsd === undefined || llmUsage.estimatedCostUsd === null) {
    return t('triagePoolDetail.aiUsage.unavailable')
  }

  const parts = [formatAiUsageCost(llmUsage.estimatedCostUsd, locale)]

  if (llmUsage.inputTokens !== undefined && llmUsage.outputTokens !== undefined) {
    parts.push(
      t('triagePoolDetail.aiUsage.tokens', {
        input: llmUsage.inputTokens.toLocaleString(locale),
        output: llmUsage.outputTokens.toLocaleString(locale),
      }),
    )
  }

  if (llmUsage.calls !== undefined && llmUsage.calls !== null) {
    parts.push(t('triagePoolDetail.aiUsage.calls', { count: llmUsage.calls }))
  }

  return parts.join(' · ')
}
