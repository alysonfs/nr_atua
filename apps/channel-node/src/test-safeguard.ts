import { normalizeWhatsAppJid } from './phone.js'

// TEMPORÁRIO — trava do teste E2E controlado do Incremento 5 (ver
// docs/implementation/PLANO-triagem-templates-channel-baileys.md, seção
// "Trava temporária do E2E"). Remover após o E2E ser validado.
export const FIXED_TEST_RECIPIENT_PHONE = '+5584994241248'

// Destinatários de teste autorizados pelo PO; espelha TriagemTestSafeguard da API.
export const ALLOWED_TEST_RECIPIENT_PHONES = [FIXED_TEST_RECIPIENT_PHONE, '+5584991050065'] as const

const allowedRecipientJids = new Set(ALLOWED_TEST_RECIPIENT_PHONES.map(normalizeWhatsAppJid))

export function isAllowedTestRecipient(recipientJid: string): boolean {
  return allowedRecipientJids.has(recipientJid)
}
