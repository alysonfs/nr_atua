import type { TFunction } from 'i18next'

const STATUS_TRANSLATION_KEYS: Record<string, string> = {
  'request cancel': 'dashboard.statuses.requestCancel',
  assigned: 'dashboard.statuses.assigned',
  designado: 'dashboard.statuses.assigned',
  accepted: 'dashboard.statuses.accepted',
  aceito: 'dashboard.statuses.accepted',
  'exchange proposal': 'dashboard.statuses.exchangeProposal',
  'exchange approved': 'dashboard.statuses.exchangeApproved',
  closed: 'dashboard.statuses.closed',
  fechado: 'dashboard.statuses.closed',
  cancelled: 'dashboard.statuses.cancelled',
  cancelado: 'dashboard.statuses.cancelled',
  'payment rejected': 'dashboard.statuses.paymentRejected',
  'request to explain': 'dashboard.statuses.requestToExplain',
  pending: 'dashboard.statuses.pending',
  pendente: 'dashboard.statuses.pending',
  'payment approved': 'dashboard.statuses.paymentApproved',
}

/** Tonalidades centralizadas (tokens `--color-status-*` em index.css). */
export type StatusTone = 'success' | 'danger' | 'info' | 'warning' | 'neutral'

const STATUS_TONES: Record<string, StatusTone> = {
  'request cancel': 'warning',
  assigned: 'info',
  designado: 'info',
  accepted: 'info',
  aceito: 'info',
  'exchange proposal': 'info',
  'exchange approved': 'success',
  closed: 'success',
  fechado: 'success',
  cancelled: 'danger',
  cancelado: 'danger',
  'payment rejected': 'danger',
  'request to explain': 'warning',
  pending: 'warning',
  pendente: 'warning',
  'payment approved': 'success',
}

const DEFAULT_TONE: StatusTone = 'neutral'

function normalizeStatus(status: string): string {
  return status.trim().toLocaleLowerCase('en-US')
}

export function translateProviderStatus(status: string, t: TFunction): string {
  const key = STATUS_TRANSLATION_KEYS[normalizeStatus(status)]
  return key ? t(key) : status
}

/** Mapeia um status cru (RF-017: string livre) para uma tonalidade centralizada. */
export function getProviderStatusTone(status: string): StatusTone {
  return STATUS_TONES[normalizeStatus(status)] ?? DEFAULT_TONE
}
