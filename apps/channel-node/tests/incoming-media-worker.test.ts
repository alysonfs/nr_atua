import assert from 'node:assert/strict'
import test from 'node:test'
import { ChannelApiError } from '../src/channel-api-client.js'
import { IncomingMediaWorker } from '../src/incoming-media-worker.js'
import type {
  ChannelApi,
  EarlyClaimTrigger,
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
    async reportSessionState() {},
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
    async reportSessionState() {},
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

test('a non-retryable ingestMedia rejection (R2) is logged and the worker keeps consuming the queue without throwing', async () => {
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
  const attempts: IncomingWhatsAppMedia[] = []
  const reported: IncomingWhatsAppMedia[] = []
  let resolveSecond: (() => void) | undefined
  const secondReported = new Promise<void>((resolve) => {
    resolveSecond = resolve
  })
  const api: ChannelApi = {
    async claim() {
      return null
    },
    async ingestMessage() {},
    async ingestMedia(value) {
      attempts.push(value)
      if (value.channelMessageId === media.channelMessageId) {
        throw new ChannelApiError('ingestMedia', 409)
      }
      reported.push(value)
      resolveSecond?.()
    },
    async reportSessionState() {},
    async complete() {
      return true
    },
  }
  const events: Array<{ event: string; fields?: Record<string, string | number | boolean> }> = []
  const captureLogger: SafeLogger = {
    info: (event, fields) => events.push({ event, fields }),
    warn: (event, fields) => events.push({ event, fields }),
    error: (event, fields) => events.push({ event, fields }),
  }
  const controller = new AbortController()
  const worker = new IncomingMediaWorker(api, receiver, captureLogger)
  const task = worker.run(controller.signal)

  listener?.(media)
  await new Promise((resolve) => setTimeout(resolve, 20))
  listener?.({ ...media, channelMessageId: 'media-message-2' })
  await secondReported
  controller.abort()
  await task

  assert.equal(attempts.length, 2)
  assert.deepEqual(reported.map((item) => item.channelMessageId), ['media-message-2'])
  assert.ok(events.some(({ event, fields }) =>
    event === 'channel_inbound_media_rejected' && fields?.statusCode === 409))
})

test('an auth-error (401/403) ingestMedia rejection is fatal and propagates out of run', async () => {
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
      throw new ChannelApiError('ingestMedia', 401)
    },
    async reportSessionState() {},
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

test('media beyond queue capacity (R1) is discarded with a log instead of growing the queue unbounded', async () => {
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
  let releaseIngest: (() => void) | undefined
  const blocked = new Promise<void>((resolve) => {
    releaseIngest = resolve
  })
  const received: IncomingWhatsAppMedia[] = []
  const api: ChannelApi = {
    async claim() {
      return null
    },
    async ingestMessage() {},
    async ingestMedia(value) {
      received.push(value)
      await blocked
    },
    async reportSessionState() {},
    async complete() {
      return true
    },
  }
  const events: Array<{ event: string; fields?: Record<string, string | number | boolean> }> = []
  const captureLogger: SafeLogger = {
    info: (event, fields) => events.push({ event, fields }),
    warn: (event, fields) => events.push({ event, fields }),
    error: (event, fields) => events.push({ event, fields }),
  }
  const controller = new AbortController()
  const worker = new IncomingMediaWorker(api, receiver, captureLogger, 1)
  const task = worker.run(controller.signal)

  // O primeiro item é imediatamente retirado da fila pelo worker e fica
  // bloqueado em ingestMedia; o segundo ocupa a única vaga da fila; o
  // terceiro deve ser descartado por capacidade.
  listener?.({ ...media, channelMessageId: 'media-message-a' })
  await new Promise((resolve) => setTimeout(resolve, 10))
  listener?.({ ...media, channelMessageId: 'media-message-b' })
  listener?.({ ...media, channelMessageId: 'media-message-c' })
  await new Promise((resolve) => setTimeout(resolve, 10))

  releaseIngest?.()
  controller.abort()
  await task

  assert.ok(events.some(({ event, fields }) =>
    event === 'channel_inbound_media_queue_full_discarded' &&
    fields?.channelMessageId === 'media-message-c'))
  assert.equal(received.some((item) => item.channelMessageId === 'media-message-c'), false)
})

test('schedules an early claim after a successful media ingest, carrying the signal and a reason', async () => {
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
  const api: ChannelApi = {
    async claim() {
      return null
    },
    async ingestMessage() {},
    async ingestMedia() {},
    async reportSessionState() {},
    async complete() {
      return true
    },
  }
  const scheduleCalls: Array<{ signal: AbortSignal; reason: string }> = []
  const earlyClaimTrigger: EarlyClaimTrigger = {
    scheduleEarlyClaim(signal, reason) {
      scheduleCalls.push({ signal, reason })
    },
  }
  const controller = new AbortController()
  const worker = new IncomingMediaWorker(api, receiver, logger, 20, earlyClaimTrigger)
  const task = worker.run(controller.signal)

  listener?.(media)
  await new Promise((resolve) => setImmediate(resolve))
  controller.abort()
  await task

  assert.equal(scheduleCalls.length, 1)
  assert.equal(scheduleCalls[0]?.reason, 'inbound_media_ingested')
  assert.equal(scheduleCalls[0]?.signal, controller.signal)
})

test('a throwing early claim trigger is logged but does not stop the media worker or lose the item', async () => {
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
  const received: IncomingWhatsAppMedia[] = []
  const api: ChannelApi = {
    async claim() {
      return null
    },
    async ingestMessage() {},
    async ingestMedia(value) {
      received.push(value)
    },
    async reportSessionState() {},
    async complete() {
      return true
    },
  }
  const earlyClaimTrigger: EarlyClaimTrigger = {
    scheduleEarlyClaim() {
      throw new Error('boom')
    },
  }
  const loggedErrors: string[] = []
  const capturingLogger: SafeLogger = {
    info: () => {},
    warn: () => {},
    error: (event) => {
      loggedErrors.push(event)
    },
  }
  const controller = new AbortController()
  const worker = new IncomingMediaWorker(api, receiver, capturingLogger, 20, earlyClaimTrigger)
  const task = worker.run(controller.signal)

  listener?.(media)
  await new Promise((resolve) => setImmediate(resolve))
  controller.abort()
  await task

  assert.deepEqual(received, [media])
  assert.ok(loggedErrors.includes('channel_early_claim_trigger_failed'))
})
