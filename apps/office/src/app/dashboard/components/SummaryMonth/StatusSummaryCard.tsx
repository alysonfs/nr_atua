import { Sparkline } from './Sparkline'

interface StatusSummaryCardProps {
  /** Nome cru do status (RF-017: string livre, sem enum fixo). */
  status: string
  /** Total de OS no status ao longo do mês corrente. */
  total: number
  /** Série diária usada para o mini-gráfico. */
  dailyCounts: number[]
  /** Cor de destaque do card (usada na linha do gráfico). */
  accentColor: string
}

/**
 * Card individual de "summary month": exibe o nome do status, a contagem
 * total no mês corrente e um mini-gráfico (sparkline) com a evolução diária.
 */
export function StatusSummaryCard({ status, total, dailyCounts, accentColor }: StatusSummaryCardProps) {
  return (
    <div className="flex flex-col gap-3 rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
      <div>
        <p className="text-xs font-semibold uppercase tracking-wider text-slate-500">{status}</p>
        <p className="mt-1 text-2xl font-bold text-slate-950">{total}</p>
        <p className="text-xs text-slate-500">OS no mês</p>
      </div>

      <Sparkline data={dailyCounts} color={accentColor} ariaLabel={`Evolução diária de OS com status ${status}`} />
    </div>
  )
}
