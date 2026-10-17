import { normalizeWhatsAppJid } from './phone.js'

// TEMPORÁRIO — trava do teste E2E controlado do Incremento 5 (ver
// docs/implementation/PLANO-triagem-templates-channel-baileys.md, seção
// "Trava temporária do E2E"). Remover após o E2E ser validado.
export const FIXED_TEST_RECIPIENT_PHONE = '+5584994241248'

const allowedRecipientJid = normalizeWhatsAppJid(FIXED_TEST_RECIPIENT_PHONE)

export function isAllowedTestRecipient(recipientJid: string): boolean {
  return recipientJid === allowedRecipientJid
}
