import assert from 'node:assert/strict'
import test from 'node:test'
import { ChannelApiError } from '../src/channel-api-client.js'
import { IncomingMediaWorker } from '../src/incoming-media-worker.js'
import type {
  ChannelApi,
  IncomingWhatsAppMedia,
  SafeLogger,
  WhatsAppMessageReceiver,
} from '../src/contracts.js'

const media: IncomingWhatsAppMedia = {
  contractVersion: '1',
  channelMessageId: 'media-message-1',
  channelIdempotencyKey: 'stable-media-key',
  correlationId: '77777777-7777-4777-8777-777777777777',
  senderPhone: '5511999991234',
  conversationKind: 'individual',
  fromMe: false,
  receivedAtUtc: '2026-10-21T12:00:00.000Z',
  mediaKind: 'audio',
  contentType: 'audio/ogg',
  sizeBytes: 3,
  sha256: 'd'.repeat(64),
  isVoiceNote: true,
  durationSeconds: 2,
  caption: null,
  content: Buffer.from([1, 2, 3]),
}

const logger: SafeLogger = {
  info: () => {},
  warn: () => {},
  error: () => {},
}

test('reports queued inbound media and releases the listener on shutdown', async () => {
  let listener: ((incoming: IncomingWhatsAppMedia) => void) | undefined
  let unsubscribeCalled = false
  const receiver: WhatsAppMessageReceiver = {
    onIncomingMessage() {
      return () => {}
    },
    onIncomingMedia(value) {
      listener = value
      return () => {
        unsubscribeCalled = true
        listener = undefined
      }
    },
  }
  let resolveReported: (() => void) | undefined
  const reported = new Promise<void>((resolve) => {
    resolveReported = resolve
  })
  const received: IncomingWhatsAppMedia[] = []
  const api: ChannelApi = {
    async claim() {
      return null
    },
    async ingestMessage() {},
    async ingestMedia(value) {
      received.push(value)
      resolveReported?.()
    },
    async complete() {
      return true
    },
  }
  const controller = new AbortController()
  const worker = new IncomingMediaWorker(api, receiver, logger)
  const task = worker.run(controller.signal)

  listener?.(media)
  await reported
  controller.abort()
  await task

  assert.deepEqual(received, [media])
  assert.equal(unsubscribeCalled, true)
  assert.equal(listener, undefined)
})

test('retries a transient ingestMedia failure with backoff and eventually succeeds', async () => {
  let listener: ((incoming: IncomingWhatsAppMedia) => void) | undefined
  const receiver: WhatsAppMessageReceiver = {
    onIncomingMessage() {
      return () => {}
    },
    onIncomingMedia(value) {
      listener = value
      return () => {}
    },
  }
  let attempts = 0
  let resolveReported: (() => void) | undefined
  const reported = new Promise<void>((resolve) => {
    resolveReported = resolve
  })
  const api: ChannelApi = {
    async claim() {
      return null
    },
    async ingestMessage() {},
    async ingestMedia() {
      attempts += 1
      if (attempts < 3) {
        throw new ChannelApiError('ingestMedia', 503)
      }
      resolveReported?.()
    },
    async complete() {
      return true
    },
  }
  const controller = new AbortController()
  const worker = new IncomingMediaWorker(api, receiver, logger)
  const task = worker.run(controller.signal)

  listener?.(media)
  await reported
  controller.abort()
  await task

  assert.equal(attempts, 3)
})

test('a non-retryable ingestMedia rejection stops the retry loop without throwing out of run', async () => {
  let listener: ((incoming: IncomingWhatsAppMedia) => void) | undefined
  const receiver: WhatsAppMessageReceiver = {
    onIncomingMessage() {
      return () => {}
    },
    onIncomingMedia(value) {
      listener = value
      return () => {}
    },
  }
  let attempts = 0
  const errors: unknown[] = []
  const api: ChannelApi = {
    async claim() {
      return null
    },
    async ingestMessage() {},
    async ingestMedia() {
      attempts += 1
      throw new ChannelApiError('ingestMedia', 409)
    },
    async complete() {
      return true
    },
  }
  const controller = new AbortController()
  const worker = new IncomingMediaWorker(api, receiver, logger)
  const task = worker.run(controller.signal).catch((error: unknown) => {
    errors.push(error)
  })

  listener?.(media)
  await new Promise((resolve) => setTimeout(resolve, 20))
  controller.abort()
  await task

  assert.equal(attempts, 1)
  assert.equal(errors.length, 1)
  assert.ok(errors[0] instanceof ChannelApiError)
})
