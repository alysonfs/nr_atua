import assert from 'node:assert/strict'
import test from 'node:test'
import { messageIdForIdempotencyKey, normalizeWhatsAppJid, PermanentChannelSendError } from '../src/phone.js'

test('normalizeWhatsAppJid strips formatting and keeps the international calling code', () => {
  assert.equal(normalizeWhatsAppJid('+55 (11) 99999-1234'), '5511999991234@s.whatsapp.net')
})

test('normalizeWhatsAppJid rejects missing or malformed destinations', () => {
  assert.throws(
    () => normalizeWhatsAppJid('not-a-phone'),
    (error: unknown) => error instanceof PermanentChannelSendError &&
      error.code === 'RECIPIENT_PHONE_INVALID',
  )
})

test('messageIdForIdempotencyKey is stable and does not expose the source key', () => {
  const key = 'tenant/integration/work-order/triage-command'
  const first = messageIdForIdempotencyKey(key)

  assert.equal(first, messageIdForIdempotencyKey(key))
  assert.notEqual(first, messageIdForIdempotencyKey(`${key}/different`))
  assert.equal(first.length, 32)
  assert.match(first, /^[A-F0-9]{32}$/)
})
