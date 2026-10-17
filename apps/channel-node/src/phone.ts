import { createHash } from 'node:crypto'

export class PermanentChannelSendError extends Error {
  readonly code: string

  constructor(code: string, message: string) {
    super(message)
    this.name = 'PermanentChannelSendError'
    this.code = code
  }
}

export function normalizeWhatsAppJid(phone: string): string {
  let digits = phone.replace(/\D/g, '')
  if (digits.startsWith('00')) {
    digits = digits.slice(2)
  }

  if (!/^[1-9]\d{7,14}$/.test(digits)) {
    throw new PermanentChannelSendError(
      'RECIPIENT_PHONE_INVALID',
      'O telefone de destino não está em formato internacional válido.',
    )
  }

  return `${digits}@s.whatsapp.net`
}

export function messageIdForIdempotencyKey(idempotencyKey: string): string {
  return createHash('sha256').update(idempotencyKey, 'utf8').digest('hex').slice(0, 32).toUpperCase()
}
