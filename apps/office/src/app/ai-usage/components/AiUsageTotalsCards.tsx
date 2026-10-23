import { useTranslation } from 'react-i18next'
import { Card } from '../../../shared/components/ui/Card'
import { formatEstimatedCost } from '../lib/formatEstimatedCost'
import type { AiUsageTotals } from '../../../shared/types/aiUsage'

interface AiUsageTotalsCardsProps {
  totals: AiUsageTotals
  currency: string
}

function Metric({ label, value }: { label: string; value: string | number }) {
  return (
    <Card>
      <Card.Body className="space-y-1">
        <p className="text-xs font-semibold uppercase tracking-wider text-slate-500">{label}</p>
        <p className="text-2xl font-bold text-slate-950">{value}</p>
      </Card.Body>
    </Card>
  )
}

/**
 * Cartões de totais do período: custo estimado, chamadas, tokens de
 * entrada/saída, latência média e falhas (`GET /api/llm-usage/summary`,
 * campo `totals`).
 */
export function AiUsageTotalsCards({ totals, currency }: AiUsageTotalsCardsProps) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'pt-BR'
  const numberFormatter = new Intl.NumberFormat(locale)

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <Metric
        label={t('aiUsage.totals.estimatedCost')}
        value={formatEstimatedCost(totals.estimatedCost, currency, locale)}
      />
      <Metric label={t('aiUsage.totals.calls')} value={numberFormatter.format(totals.calls)} />
      <Metric label={t('aiUsage.totals.inputTokens')} value={numberFormatter.format(totals.inputTokens)} />
      <Metric label={t('aiUsage.totals.outputTokens')} value={numberFormatter.format(totals.outputTokens)} />
      <Metric
        label={t('aiUsage.totals.avgLatencyMs')}
        value={t('aiUsage.totals.avgLatencyValue', { value: numberFormatter.format(Math.round(totals.avgLatencyMs)) })}
      />
      <Metric label={t('aiUsage.totals.failedCalls')} value={numberFormatter.format(totals.failedCalls)} />
    </div>
  )
}
