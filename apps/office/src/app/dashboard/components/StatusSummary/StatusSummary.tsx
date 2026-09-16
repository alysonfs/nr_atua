import { useWorkOrderStatusSummary } from '../../hooks/useWorkOrderStatusSummary'
import { StatusSummaryCard } from './StatusSummaryCard'
import { useTranslation } from 'react-i18next'
import { getProviderStatusAccentColor, translateProviderStatus } from '../../lib/providerStatus'

interface StatusSummaryProps {
  /** Tenant ativo do usuário (ver useMyTenants().defaultTenantId). */
  tenantId: string | null
  /** Status atualmente selecionado (raw, sem tradução) para destacar o card correspondente. */
  selectedStatus: string
  /** Disparado ao clicar em um card, com o status raw (sem tradução). */
  onSelectStatus: (status: string) => void
}

/**
 * Seção "resumo por status" do Dashboard: mostra a contagem de OS agrupada
 * pelo status ATUAL (RF-017), sem recorte de mês/série diária (ADR-027).
 *
 * Integra com GET /api/tenants/{tenantId}/work-orders/status-summary (ver
 * useWorkOrderStatusSummary). Cada card é clicável e filtra a tabela de OS
 * abaixo pelo status selecionado.
 */
export function StatusSummary({ tenantId, selectedStatus, onSelectStatus }: StatusSummaryProps) {
  const { t } = useTranslation()
  const { summary, isLoading, isError } = useWorkOrderStatusSummary(tenantId)

  return (
    <section aria-labelledby="status-summary-heading" className="space-y-3">
      <h2 id="status-summary-heading" className="text-sm font-semibold text-slate-700">
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
          {summary.map(({ status, total }) => (
            <StatusSummaryCard
              key={status}
              status={translateProviderStatus(status, t)}
              total={total}
              accentColor={getProviderStatusAccentColor(status)}
              isSelected={status.toLocaleLowerCase() === selectedStatus.toLocaleLowerCase()}
              onSelect={() => onSelectStatus(status)}
            />
          ))}
        </div>
      )}
    </section>
  )
}
