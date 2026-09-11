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

/**
 * Seção "summary month" do Dashboard: mostra, para o mês corrente, a
 * contagem de OS agrupada por status (string livre, RF-017), cada uma em
 * um card com mini-gráfico (sparkline) da evolução diária.
 *
 * Os dados exibidos são mockados (ver useServiceOrderMonthSummary); a
 * integração com o endpoint real de agregação mensal é uma etapa futura.
 */
export function SummaryMonth() {
  const summary = useServiceOrderMonthSummary()

  return (
    <section aria-labelledby="summary-month-heading" className="space-y-3">
      <h2 id="summary-month-heading" className="text-sm font-semibold text-slate-700">
        Resumo do mês
      </h2>

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
    </section>
  )
}
