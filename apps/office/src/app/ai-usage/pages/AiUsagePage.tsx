import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AiUsagePeriodSelector } from '../components/AiUsagePeriodSelector'
import { AiUsageTotalsCards } from '../components/AiUsageTotalsCards'
import { AiUsageTenantsTable } from '../components/AiUsageTenantsTable'
import { AiUsageModelsTable } from '../components/AiUsageModelsTable'
import { AiUsageDailyChart } from '../components/AiUsageDailyChart'
import { useAiUsageSummary } from '../hooks/useAiUsageSummary'
import { resolveAiUsageRange, type AiUsagePeriod } from '../lib/aiUsagePeriod'

/**
 * Página `/ai-usage`: consumo e custo estimado de IA (LLM) por tenant, por
 * período (7/30/90 dias ou mês corrente) — `GET /api/llm-usage/summary`.
 *
 * O custo é estimado a partir do preço público por token de cada modelo; a
 * fatura da AWS permanece a referência oficial de cobrança (aviso fixo na
 * página).
 */
export function AiUsagePage() {
  const { t } = useTranslation()
  const [period, setPeriod] = useState<AiUsagePeriod>('30d')

  const { from, to } = useMemo(() => resolveAiUsageRange(period), [period])
  const { summary, isLoading, isError } = useAiUsageSummary(from, to)

  return (
    <div className="grid w-full grid-cols-12 gap-4">
      <div className="col-span-12 border-b border-slate-200 pb-5">
        <h1 className="text-2xl font-semibold text-slate-950 sm:text-3xl">{t('aiUsage.title')}</h1>
        <p className="mt-1 max-w-2xl text-sm text-slate-600">{t('aiUsage.subtitle')}</p>
      </div>

      <div className="col-span-12">
        <AiUsagePeriodSelector value={period} onChange={setPeriod} />
      </div>

      <p className="col-span-12 text-xs text-slate-500">{t('aiUsage.costDisclaimer')}</p>

      {isLoading && (
        <p className="col-span-12 rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('aiUsage.loading')}
        </p>
      )}

      {!isLoading && isError && (
        <p
          role="alert"
          className="col-span-12 rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm"
        >
          {t('aiUsage.loadError')}
        </p>
      )}

      {!isLoading && !isError && summary && (
        <>
          <div className="col-span-12">
            <AiUsageTotalsCards totals={summary.totals} currency={summary.currency} />
          </div>

          <div className="col-span-12">
            <AiUsageDailyChart daily={summary.daily} />
          </div>

          <div className="col-span-12">
            <AiUsageTenantsTable tenants={summary.tenants} currency={summary.currency} />
          </div>

          <div className="col-span-12">
            <AiUsageModelsTable models={summary.models} currency={summary.currency} />
          </div>
        </>
      )}

      {!isLoading && !isError && !summary && (
        <p className="col-span-12 rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('aiUsage.empty')}
        </p>
      )}
    </div>
  )
}
