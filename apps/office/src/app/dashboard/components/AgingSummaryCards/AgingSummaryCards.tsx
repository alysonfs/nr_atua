import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useWorkOrderAgingSummary } from '../../hooks/useWorkOrderAgingSummary'

interface AgingSummaryCardsProps {
  /** Tenant ativo do usuário (ver useMyTenants().defaultTenantId). */
  tenantId: string | null
}

interface AgingCardProps {
  days: number
  total: number
  label: string
}

/**
 * Card individual de aging: apenas navega (drill-down) para
 * `/service-orders` com o filtro de aging pré-aplicado — distinto de
 * `StatusSummaryCard`, que alterna estado local de seleção dentro da mesma
 * página.
 */
function AgingCard({ days, total, label }: AgingCardProps) {
  return (
    <Link
      to={`/service-orders?minAgeDays=${days}`}
      className="flex flex-col gap-2 rounded-lg border border-l-4 border-l-status-warning border-slate-200 bg-white p-4 shadow-sm transition-colors hover:bg-slate-50"
    >
      <span className="text-sm font-medium text-slate-600">{label}</span>
      <p className="text-2xl font-bold text-slate-950">{total}</p>
    </Link>
  )
}

/**
 * Seção "aging" do Dashboard (RF-027): 3 cards com a contagem de OS abertas
 * há mais de 5/10/30 dias, cumulativos e não mutuamente exclusivos entre si.
 * Cada card navega para `/service-orders` já filtrado pela faixa de aging
 * clicada.
 */
export function AgingSummaryCards({ tenantId }: AgingSummaryCardsProps) {
  const { t } = useTranslation()
  const { summary, isLoading, isError } = useWorkOrderAgingSummary(tenantId)

  return (
    <section aria-labelledby="aging-summary-heading" className="space-y-3">
      <h2 id="aging-summary-heading" className="text-sm font-semibold text-slate-700">
        {t('dashboard.aging.title')}
      </h2>

      {isLoading && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('dashboard.aging.loading')}
        </p>
      )}

      {!isLoading && isError && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          {t('dashboard.aging.error')}
        </p>
      )}

      {!isLoading && !isError && summary && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <AgingCard days={5} total={summary.moreThan5Days} label={t('dashboard.aging.moreThan5Days')} />
          <AgingCard days={10} total={summary.moreThan10Days} label={t('dashboard.aging.moreThan10Days')} />
          <AgingCard days={30} total={summary.moreThan30Days} label={t('dashboard.aging.moreThan30Days')} />
        </div>
      )}
    </section>
  )
}
