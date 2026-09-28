import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useWorkOrderCriticalitySummary } from '../../hooks/useWorkOrderCriticalitySummary'

interface CriticalitySummaryCardsProps {
  /** Tenant ativo do usuário (ver useMyTenants().defaultTenantId). */
  tenantId: string | null
}

type CriticalityFilter = 'critical' | 'at-risk'
type CriticalityTone = 'danger' | 'warning'

interface CriticalityCardProps {
  filter: CriticalityFilter
  total: number
  label: string
  tone: CriticalityTone
}

const TONE_BORDER_CLASSES: Record<CriticalityTone, string> = {
  danger: 'border-l-status-danger',
  warning: 'border-l-status-warning',
}

/**
 * Card individual de criticidade: apenas navega (drill-down) para
 * `/service-orders` com o filtro de criticidade pré-aplicado (RF-033.4).
 */
function CriticalityCard({ filter, total, label, tone }: CriticalityCardProps) {
  return (
    <Link
      to={`/service-orders?criticalityFilter=${filter}`}
      className={`flex flex-col gap-2 rounded-lg border border-l-4 ${TONE_BORDER_CLASSES[tone]} border-slate-200 bg-white p-4 shadow-sm transition-colors hover:bg-slate-50`}
    >
      <span className="text-sm font-medium text-slate-600">{label}</span>
      <p className="text-2xl font-bold text-slate-950">{total}</p>
    </Link>
  )
}

/**
 * Seção "situação crítica e risco" do Dashboard (RF-033): 2 cards com a
 * contagem de OS Em Aberto sem atualização há mais de 5 dias (crítica) e
 * entre 4 e 5 dias (risco de criticidade). Cada card navega para
 * `/service-orders` já filtrado.
 */
export function CriticalitySummaryCards({ tenantId }: CriticalitySummaryCardsProps) {
  const { t } = useTranslation()
  const { summary, isLoading, isError } = useWorkOrderCriticalitySummary(tenantId)

  return (
    <section aria-labelledby="criticality-summary-heading" className="space-y-3">
      <h2 id="criticality-summary-heading" className="text-sm font-semibold text-slate-700">
        {t('dashboard.criticality.title')}
      </h2>

      {isLoading && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('dashboard.criticality.loading')}
        </p>
      )}

      {!isLoading && isError && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          {t('dashboard.criticality.error')}
        </p>
      )}

      {!isLoading && !isError && summary && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <CriticalityCard
            filter="critical"
            total={summary.critical}
            label={t('dashboard.criticality.critical')}
            tone="danger"
          />
          <CriticalityCard
            filter="at-risk"
            total={summary.atRisk}
            label={t('dashboard.criticality.atRisk')}
            tone="warning"
          />
        </div>
      )}
    </section>
  )
}
