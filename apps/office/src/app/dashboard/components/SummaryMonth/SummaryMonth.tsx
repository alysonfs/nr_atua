import { useServiceOrderMonthSummary } from '../../hooks/useServiceOrderMonthSummary'
import { StatusSummaryCard } from './StatusSummaryCard'

/**
 * Paleta de destaque por status, alinhada com a paleta já usada no app
 * (slate/sky no cabeçalho, com variações semânticas por situação).
 */
const STATUS_ACCENT_COLOR: Record<string, string> = {
  Designado: '#0369a1', // sky-700
  'Em Processamento': '#b45309', // amber-700
  Concluído: '#15803d', // green-700
  Cancelado: '#b91c1c', // red-700
}

const DEFAULT_ACCENT_COLOR = '#334155' // slate-700

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
  const { summary, isLoading, isError } = useServiceOrderMonthSummary(tenantId)

  return (
    <section aria-labelledby="summary-month-heading" className="space-y-3">
      <h2 id="summary-month-heading" className="text-sm font-semibold text-slate-700">
        Resumo do mês
      </h2>

      {isLoading && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          Carregando resumo do mês...
        </p>
      )}

      {!isLoading && isError && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          Não foi possível carregar o resumo do mês. Tente novamente mais tarde.
        </p>
      )}

      {!isLoading && !isError && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {summary.map(({ status, total, dailyCounts }) => (
            <StatusSummaryCard
              key={status}
              status={status}
              total={total}
              dailyCounts={dailyCounts}
              accentColor={STATUS_ACCENT_COLOR[status] ?? DEFAULT_ACCENT_COLOR}
            />
          ))}
        </div>
      )}
    </section>
  )
}
