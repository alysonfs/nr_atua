import type { TFunction } from 'i18next'

const STATUS_TRANSLATION_KEYS: Record<string, string> = {
  'request cancel': 'dashboard.statuses.requestCancel',
  assigned: 'dashboard.statuses.assigned',
  designado: 'dashboard.statuses.assigned',
  'exchange proposal': 'dashboard.statuses.exchangeProposal',
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

const STATUS_ACCENT_COLORS: Record<string, string> = {
  'request cancel': '#c2410c',
  assigned: '#0369a1',
  designado: '#0369a1',
  'exchange proposal': '#7c3aed',
  closed: '#15803d',
  fechado: '#15803d',
  cancelled: '#b91c1c',
  cancelado: '#b91c1c',
  'payment rejected': '#be123c',
  'request to explain': '#a16207',
  pending: '#b45309',
  pendente: '#b45309',
  'payment approved': '#047857',
}

const DEFAULT_ACCENT_COLOR = '#334155'

function normalizeStatus(status: string): string {
  return status.trim().toLocaleLowerCase('en-US')
}

export function translateProviderStatus(status: string, t: TFunction): string {
  const key = STATUS_TRANSLATION_KEYS[normalizeStatus(status)]
  return key ? t(key) : status
}

export function getProviderStatusAccentColor(status: string): string {
  return STATUS_ACCENT_COLORS[normalizeStatus(status)] ?? DEFAULT_ACCENT_COLOR
}
