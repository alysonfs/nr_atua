interface StatusSummaryCardProps {
  /** Nome cru do status (RF-017: string livre, sem enum fixo). */
  status: string
  /** Total de OS atualmente nesse status. */
  total: number
  /** Cor de destaque do card. */
  accentColor: string
}

/** Card individual do resumo por status: nome do status e contagem total atual (ADR-027, sem série diária). */
export function StatusSummaryCard({ status, total, accentColor }: StatusSummaryCardProps) {
  return (
    <div
      className="flex flex-col gap-1 rounded-lg border border-slate-200 bg-white p-4 shadow-sm"
      style={{ borderLeft: `4px solid ${accentColor}` }}
    >
      <p className="text-xs font-semibold uppercase tracking-wider text-slate-500">{status}</p>
      <p className="mt-1 text-2xl font-bold text-slate-950">{total}</p>
    </div>
  )
}
