interface StatusSummaryCardProps {
  /** Nome cru do status (RF-017: string livre, sem enum fixo). */
  status: string
  /** Total de OS atualmente nesse status. */
  total: number
  /** Cor de destaque do card. */
  accentColor: string
  /** Se este é o status atualmente selecionado na tabela de OS abaixo. */
  isSelected: boolean
  /** Disparado ao clicar no card, para filtrar a tabela de OS por este status. */
  onSelect: () => void
}

/**
 * Card individual do resumo por status: nome do status e contagem total
 * atual (ADR-027, sem série diária). Clicável: seleciona o status para
 * filtrar a tabela de OS abaixo.
 */
export function StatusSummaryCard({ status, total, accentColor, isSelected, onSelect }: StatusSummaryCardProps) {
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={isSelected}
      className={`flex flex-col gap-1 rounded-lg border bg-white p-4 text-left shadow-sm transition-colors hover:bg-slate-50 ${
        isSelected ? 'border-slate-400 ring-1 ring-slate-300' : 'border-slate-200'
      }`}
      style={{ borderLeft: `4px solid ${accentColor}` }}
    >
      <p className="text-xs font-semibold uppercase tracking-wider text-slate-500">{status}</p>
      <p className="mt-1 text-2xl font-bold text-slate-950">{total}</p>
    </button>
  )
}
