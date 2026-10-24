import assert from 'node:assert/strict'
import { EventEmitter } from 'node:events'
import test from 'node:test'
import { initAuthCreds, proto, type UserFacingSocketConfig } from '@whiskeysockets/baileys'
import { createBaileysSocket } from '../src/baileys-session.js'
import type { SafeLogger } from '../src/contracts.js'
import { OutboundMessageCache } from '../src/outbound-message-cache.js'

function createHarness() {
  const events: Array<{ event: string; fields?: Record<string, string | number | boolean> }> = []
  const logger: SafeLogger = {
    info: (event, fields) => events.push({ event, fields }),
    warn: (event, fields) => events.push({ event, fields }),
    error: (event, fields) => events.push({ event, fields }),
  }
  const ev = new EventEmitter()
  const cache = new OutboundMessageCache(logger)
  let config!: UserFacingSocketConfig
  const socket = createBaileysSocket({
    creds: initAuthCreds(), keys: { get: async () => ({}), set: async () => {} },
  }, logger, cache, (configuration) => {
    config = configuration
    return {
      ev, ws: new EventEmitter(), onWhatsApp: async () => [],
      sendMessage: async () => undefined, end: () => {},
    }
  })
  return { ev, events, socket, config }
}

test('distinguishes server acceptance, device delivery, read and error without logging content', () => {
  const { ev, events, socket } = createHarness()
  const statuses = proto.WebMessageInfo.Status
  for (const status of [statuses.SERVER_ACK, statuses.DELIVERY_ACK, statuses.READ, statuses.PLAYED, statuses.ERROR]) {
    ev.emit('messages.update', [{
      key: { id: 'TEST-123', fromMe: true, remoteJid: 'private-recipient@s.whatsapp.net' },
      update: { status, message: { conversation: 'private content' } },
    }])
  }
  assert.deepEqual(events.map(({ fields }) => fields?.deliveryStatus), ['server_ack', 'delivered', 'read', 'played', 'error'])
  assert.ok(events.every(({ event, fields }) => event === 'whatsapp_message_delivery_update' && fields?.messageId === 'TEST-123'))
  assert.equal(JSON.stringify(events).includes('private'), false)
  socket.end()
})

test('does not infer delivery from server ACK or log inbound and unrelated updates', () => {
  const { ev, events, socket } = createHarness()
  const status = proto.WebMessageInfo.Status.DELIVERY_ACK
  ev.emit('messages.update', [
    { key: { id: 'INBOUND', fromMe: false }, update: { status } },
    { key: { id: 'UNKNOWN' }, update: { status } },
    { key: { id: 'unsafe\nvalue', fromMe: true }, update: { status } },
    { key: { fromMe: true }, update: { status } },
    { key: { id: 'NO-STATUS', fromMe: true }, update: {} },
    { key: { id: 'SERVER', fromMe: true }, update: { status: proto.WebMessageInfo.Status.SERVER_ACK } },
  ])
  assert.deepEqual(events, [{ event: 'whatsapp_message_delivery_update', fields: { messageId: 'SERVER', deliveryStatus: 'server_ack' } }])
  socket.end()
})

test('removes delivery listener when socket is ended', () => {
  const { ev, events, socket } = createHarness()
  assert.equal(ev.listenerCount('messages.update'), 1)
  socket.end()
  assert.equal(ev.listenerCount('messages.update'), 0)
  ev.emit('messages.update', [{ key: { id: 'OLD', fromMe: true }, update: { status: proto.WebMessageInfo.Status.READ } }])
  assert.equal(events.length, 0)
})

test('reports retry session reconstruction without exposing SDK recipient or key material', () => {
  const { config, events, socket } = createHarness()
  config.logger?.debug({ participant: 'private@s.whatsapp.net', key: 'private-key' }, 'forced new session for retry recp')
  assert.deepEqual(events, [{ event: 'whatsapp_retry_session_rebuilt', fields: undefined }])
  socket.end()
})
