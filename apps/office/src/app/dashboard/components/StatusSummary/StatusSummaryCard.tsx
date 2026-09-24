import { Badge } from '../../../../shared/components/ui/Badge'
import type { StatusTone } from '../../lib/providerStatus'

interface StatusSummaryCardProps {
  /** Nome cru do status (RF-017: string livre, sem enum fixo). */
  status: string
  /** Total de OS atualmente nesse status. */
  total: number
  /** Tonalidade centralizada do status (ver providerStatus.ts). */
  tone: StatusTone
  /** Se este é o status atualmente selecionado na tabela de OS abaixo. */
  isSelected: boolean
  /** Disparado ao clicar no card, para filtrar a tabela de OS por este status. */
  onSelect: () => void
}

const TONE_BORDER_CLASSES: Record<StatusTone, string> = {
  success: 'border-l-status-success',
  danger: 'border-l-status-danger',
  info: 'border-l-status-info',
  warning: 'border-l-status-warning',
  neutral: 'border-l-status-neutral',
}

/**
 * Card individual do resumo por status: nome do status e contagem total
 * atual (ADR-027, sem série diária). Clicável: seleciona o status para
 * filtrar a tabela de OS abaixo.
 */
export function StatusSummaryCard({ status, total, tone, isSelected, onSelect }: StatusSummaryCardProps) {
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={isSelected}
      className={`flex flex-col gap-2 rounded-lg border border-l-4 bg-white p-4 text-left shadow-sm transition-colors hover:bg-slate-50 ${
        TONE_BORDER_CLASSES[tone]
      } ${isSelected ? 'border-slate-400 ring-1 ring-slate-300' : 'border-slate-200'}`}
    >
      <Badge tone={tone}>{status}</Badge>
      <p className="text-2xl font-bold text-slate-950">{total}</p>
    </button>
  )
}
