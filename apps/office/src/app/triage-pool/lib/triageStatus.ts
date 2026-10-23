import type { TFunction } from 'i18next'
import type { StatusTone } from '../../dashboard/lib/providerStatus'
import type {
  TriagemInProgressStatus,
  TriagemListScope,
  TriagemListStatus,
  TriagemPoolStatus,
} from '../../../shared/types/triagePool'

/**
 * Os estados conhecidos (pool, "em triagem"/pré-pool e demais) possuem
 * rótulo traduzido em `triagePool.status.*` (ver `src/i18n.ts`). Estados
 * desconhecidos caem no próprio valor bruto como fallback.
 */
const STATUS_TONES: Record<string, StatusTone> = {
  LiberadaAoPool: 'info',
  Expirada: 'danger',
  FalhaDeContato: 'warning',
  AguardandoIntervencao: 'warning',
  ContatoIniciado: 'neutral',
  EmColeta: 'neutral',
  InformacoesSuficientes: 'info',
  AssumidaPorOperador: 'success',
}

export function translateTriagemPoolStatus(t: TFunction, status: string): string {
  return t(`triagePool.status.${status}`, status)
}

export function triagemPoolStatusTone(status: string): StatusTone {
  return STATUS_TONES[status] ?? 'neutral'
}

/** As quatro opções de filtro de estado do pool (épico, "C1 — Lista do pool"). */
export const TRIAGE_POOL_STATUS_FILTERS: TriagemPoolStatus[] = [
  'LiberadaAoPool',
  'Expirada',
  'FalhaDeContato',
  'AguardandoIntervencao',
]

/** Opções de filtro dos estados "em triagem" (pré-pool), visíveis no Office só para leitura. */
export const TRIAGE_IN_PROGRESS_STATUS_FILTERS: TriagemInProgressStatus[] = [
  'ContatoIniciado',
  'EmColeta',
  'InformacoesSuficientes',
]

export function statusFiltersForScope(scope: TriagemListScope): TriagemListStatus[] {
  if (scope === 'in-progress') return TRIAGE_IN_PROGRESS_STATUS_FILTERS
  if (scope === 'all') return [...TRIAGE_IN_PROGRESS_STATUS_FILTERS, ...TRIAGE_POOL_STATUS_FILTERS]
  return TRIAGE_POOL_STATUS_FILTERS
}

export function translateChecklistItem(t: TFunction, item: string): string {
  return t(`triagePoolDetail.checklist.items.${item}`, item)
}

export function translateConversationSender(t: TFunction, sender: string): string {
  return t(`triagePoolDetail.conversation.sender.${sender}`, sender)
}
