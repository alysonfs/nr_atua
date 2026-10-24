import assert from 'node:assert/strict'
import { EventEmitter } from 'node:events'
import test from 'node:test'
import { initAuthCreds, proto, type AuthenticationState, type UserFacingSocketConfig, type WAMessage } from '@whiskeysockets/baileys'
import { createBaileysSocket } from '../src/baileys-session.js'
import type { SafeLogger } from '../src/contracts.js'
import { MAX_OUTBOUND_RETRY_MESSAGES, OUTBOUND_RETRY_TTL_MS, OutboundMessageCache } from '../src/outbound-message-cache.js'

const recipient = '5511999991234@s.whatsapp.net'
const canonicalRecipient = '551199991234@s.whatsapp.net'
const lid = '1234567890@lid'

function message(id: string): WAMessage {
  return {
    key: { id, remoteJid: canonicalRecipient, fromMe: true },
    message: {
      extendedTextMessage: {
        text: 'private test content',
        contextInfo: { stanzaId: 'original-context' },
      },
    },
  }
}

function createCache() {
  const events: Array<{ event: string; fields?: Record<string, string | number | boolean> }> = []
  const logger: SafeLogger = {
    info: (event, fields) => events.push({ event, fields }),
    warn: (event, fields) => events.push({ event, fields }),
    error: (event, fields) => events.push({ event, fields }),
  }
  return { cache: new OutboundMessageCache(logger), logger, events }
}

test('retains the exact original payload for known PN/LID aliases and rejects other recipients', async () => {
  const { cache, events } = createCache()
  const original = message('outbound-1')
  try {
    await cache.send(canonicalRecipient, 'outbound-1', [recipient, lid], async () => original)
    for (const remoteJid of [recipient, canonicalRecipient, '551199991234:4@s.whatsapp.net', '1234567890:7@lid']) {
      assert.deepEqual(await cache.getMessage({ id: 'outbound-1', remoteJid, fromMe: true }), original.message)
    }
    assert.equal(await cache.getMessage({ id: 'outbound-1', remoteJid: '5511888881234@s.whatsapp.net' }), undefined)
    assert.equal(await cache.getMessage({ id: 'outbound-1', remoteJid: recipient, fromMe: false }), undefined)
    assert.equal(await cache.getMessage({ id: 'missing', remoteJid: recipient }), undefined)
    assert.equal(await cache.getMessage({ remoteJid: recipient }), undefined)
    const serialized = JSON.stringify(events)
    assert.equal(serialized.includes('private test content'), false)
    assert.equal(serialized.includes('@s.whatsapp.net'), false)
    assert.equal(serialized.includes('@lid'), false)
  } finally {
    cache.clear()
  }
})

test('a retry arriving during send waits for the original generated message', async () => {
  const { cache } = createCache()
  let complete!: (value: WAMessage) => void
  const sending = cache.send(recipient, 'pending', [], () => new Promise((resolve) => { complete = resolve }))
  const retry = cache.getMessage({ id: 'pending', remoteJid: recipient, fromMe: true })
  complete(message('pending'))
  await sending
  assert.deepEqual(await retry, message('pending').message)
  cache.clear()
})

test('failed sends unblock pending lookups and surface a diagnostic', async () => {
  const { cache, events } = createCache()
  let fail!: (error: Error) => void
  const sending = cache.send(recipient, 'failed', [], () => new Promise((_resolve, reject) => { fail = reject }))
  const retry = cache.getMessage({ id: 'failed', remoteJid: recipient })
  const rejected = assert.rejects(sending, /test failure/)
  fail(new Error('test failure'))
  await rejected
  assert.equal(await retry, undefined)
  assert.ok(events.some(({ event }) => event === 'whatsapp_retry_cache_send_failed'))
})

test('missing original payload cannot be reported as a successful cached send', async () => {
  const { cache } = createCache()
  await assert.rejects(cache.send(recipient, 'empty', [], async () => undefined), /mensagem original/)
  await assert.rejects(cache.send(recipient, 'wrong-id', [], async () => message('other')), /mensagem original/)
  assert.equal(await cache.getMessage({ id: 'empty', remoteJid: recipient }), undefined)
})

test('expires at exactly one hour even after lookups and repeated sends, without new activity', async (t) => {
  t.mock.timers.enable({ apis: ['Date', 'setTimeout'], now: 1_000 })
  const { cache, events } = createCache()
  await cache.send(recipient, 'expiring', [], async () => message('expiring'))
  t.mock.timers.tick(OUTBOUND_RETRY_TTL_MS - 1)
  assert.deepEqual(await cache.getMessage({ id: 'expiring', remoteJid: recipient }), message('expiring').message)
  await cache.send(recipient, 'expiring', [], async () => message('expiring'))
  t.mock.timers.tick(1)
  assert.equal(await cache.getMessage({ id: 'expiring', remoteJid: recipient }), undefined)
  assert.ok(events.some(({ event, fields }) =>
    event === 'whatsapp_retry_cache_evicted' && fields?.reason === 'expired'))
})

test('capacity is bounded to 256 messages and evicts the oldest, not the newest', async () => {
  const { cache, events } = createCache()
  try {
    for (let index = 0; index <= MAX_OUTBOUND_RETRY_MESSAGES; index += 1) {
      await cache.send(recipient, `message-${index}`, [], async () => message(`message-${index}`))
    }
    assert.equal(await cache.getMessage({ id: 'message-0', remoteJid: recipient }), undefined)
    assert.deepEqual(
      await cache.getMessage({ id: 'message-1', remoteJid: recipient }),
      message('message-1').message,
    )
    assert.deepEqual(
      await cache.getMessage({ id: `message-${MAX_OUTBOUND_RETRY_MESSAGES}`, remoteJid: recipient }),
      message(`message-${MAX_OUTBOUND_RETRY_MESSAGES}`).message,
    )
    assert.ok(events.some(({ event, fields }) =>
      event === 'whatsapp_retry_cache_evicted' && fields?.reason === 'capacity'))
  } finally {
    cache.clear()
  }
})

test('cache is isolated per session and clearing releases pending lookups', async () => {
  const first = createCache().cache
  const second = createCache().cache
  try {
    await first.send(recipient, 'isolated', [], async () => message('isolated'))
    assert.equal(await second.getMessage({ id: 'isolated', remoteJid: recipient }), undefined)
    const sending = first.send(recipient, 'pending', [], async () => {
      first.clear()
      return message('pending')
    })
    assert.equal(await first.getMessage({ id: 'pending', remoteJid: recipient }), undefined)
    await sending
    assert.equal(await first.getMessage({ id: 'isolated', remoteJid: recipient }), undefined)
  } finally {
    first.clear()
    second.clear()
  }
})

test('production adapter configures getMessage, preserves retry across socket reconnects and sanitizes SDK diagnostics', async () => {
  const { cache, logger, events } = createCache()
  const auth: AuthenticationState = {
    creds: initAuthCreds(),
    keys: { get: async () => ({}), set: async () => {} },
  }
  let configuration: UserFacingSocketConfig | undefined
  let retryDuringSend: Promise<proto.IMessage | undefined> | undefined
  const createAdapter = () => createBaileysSocket(auth, logger, cache, (config) => {
    configuration = config
    return {
      ev: new EventEmitter(),
      ws: new EventEmitter(),
      onWhatsApp: async () => [{ exists: true, jid: canonicalRecipient, lid }],
      sendMessage: async (_jid, _content, options) => {
        assert.equal(options?.messageId, 'adapter-id')
        retryDuringSend = config.getMessage?.({ id: 'adapter-id', remoteJid: lid, fromMe: true })
        return proto.WebMessageInfo.create(message('adapter-id'))
      },
      end: () => {},
    }
  })

  try {
    const adapter = createAdapter()
    const resolved = await adapter.resolveRecipient(recipient)
    assert.deepEqual(resolved, { jid: canonicalRecipient, aliases: [recipient, lid] })
    await adapter.sendMessage(resolved.jid, 'private test content', 'adapter-id', resolved.aliases)
    assert.deepEqual(await retryDuringSend, message('adapter-id').message)
    const reconnected = createAdapter()
    assert.ok(configuration?.getMessage)
    assert.deepEqual(
      await configuration.getMessage({ id: 'adapter-id', remoteJid: lid, fromMe: true }),
      message('adapter-id').message,
    )
    configuration.logger?.debug({ jid: recipient, secret: 'private key' }, 'recv retry request')
    configuration.logger?.debug({ jid: recipient }, 'recv retry request, but message not available')
    configuration.logger?.error({ trace: 'private key' }, 'error in sending message again')
    configuration.logger?.warn({ secret: 'private key' }, 'private test content')
    configuration.logger?.error({ secret: 'private key' }, 'will not send message again, as sent too many times')
    configuration.logger?.info({ text: 'private test content' }, 'unrelated SDK event')
    assert.ok(events.some(({ event }) => event === 'whatsapp_retry_requested'))
    assert.ok(events.some(({ event }) => event === 'whatsapp_retry_original_message_missing'))
    assert.ok(events.some(({ event }) => event === 'whatsapp_retry_send_failed'))
    assert.ok(events.some(({ event }) => event === 'whatsapp_retry_limit_reached'))
    assert.ok(events.some(({ event }) => event === 'whatsapp_sdk_warning'))
    const serialized = JSON.stringify(events)
    for (const secret of [recipient, 'private key', 'private test content']) {
      assert.equal(serialized.includes(secret), false)
    }
    reconnected.end()
    adapter.end()
  } finally {
    cache.clear()
  }
})

test('falls back to recovery only when the original is not cached', async () => {
  const { cache, events } = createCache()
  const recovered = proto.Message.fromObject({ extendedTextMessage: { text: 'recovered' } })
  const calls: string[] = []
  cache.setRecovery(async (key) => {
    calls.push(key.id ?? '')
    return key.id === 'KNOWN' ? recovered : undefined
  })

  assert.equal(await cache.getMessage({ id: 'KNOWN', remoteJid: recipient, fromMe: true }), recovered)
  assert.equal(await cache.getMessage({ id: 'UNKNOWN', remoteJid: recipient, fromMe: true }), undefined)
  assert.equal(await cache.getMessage({ id: 'KNOWN', remoteJid: recipient, fromMe: false }), undefined)
  assert.deepEqual(calls, ['KNOWN', 'UNKNOWN'])
  assert.ok(events.some((entry) => entry.event === 'whatsapp_retry_message_recovered'))
})

test('recovery failures never throw into the SDK', async () => {
  const { cache, events } = createCache()
  cache.setRecovery(async () => { throw new Error('api down') })

  assert.equal(await cache.getMessage({ id: 'KNOWN', remoteJid: recipient, fromMe: true }), undefined)
  assert.ok(events.some((entry) => entry.event === 'whatsapp_retry_recovery_failed'))
})
