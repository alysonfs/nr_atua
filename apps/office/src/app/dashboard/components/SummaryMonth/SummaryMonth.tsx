import { useServiceOrderMonthSummary } from '../../hooks/useServiceOrderMonthSummary'
import { StatusSummaryCard } from './StatusSummaryCard'
import { useTranslation } from 'react-i18next'
import { getProviderStatusAccentColor, translateProviderStatus } from '../../lib/providerStatus'

interface SummaryMonthProps {
  /** Tenant ativo do usuário (ver useMyTenants().defaultTenantId). */
  tenantId: string | null
}

/**
 * Seção "summary month" do Dashboard: mostra, para o mês corrente, a
 * contagem de OS agrupada por status (string livre, RF-017), cada uma em
 * um card com mini-gráfico (sparkline) da evolução diária.
 *
 * Integra com GET /api/tenants/{tenantId}/work-orders/summary (ver
 * useServiceOrderMonthSummary).
 */
export function SummaryMonth({ tenantId }: SummaryMonthProps) {
  const { t } = useTranslation()
  const { summary, isLoading, isError } = useServiceOrderMonthSummary(tenantId)

  return (
    <section aria-labelledby="summary-month-heading" className="space-y-3">
      <h2 id="summary-month-heading" className="text-sm font-semibold text-slate-700">
        {t('dashboard.summaryTitle')}
      </h2>

      {isLoading && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('dashboard.summaryLoading')}
        </p>
      )}

      {!isLoading && isError && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          {t('dashboard.summaryError')}
        </p>
      )}

      {!isLoading && !isError && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {summary.map(({ status, total, dailyCounts }) => (
            <StatusSummaryCard
              key={status}
              status={translateProviderStatus(status, t)}
              total={total}
              dailyCounts={dailyCounts}
              accentColor={getProviderStatusAccentColor(status)}
            />
          ))}
        </div>
      )}
    </section>
  )
}
