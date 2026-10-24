import assert from 'node:assert/strict'
import test from 'node:test'
import { IncomingMessageWorker } from '../src/incoming-message-worker.js'
import type {
  ChannelApi,
  EarlyClaimTrigger,
  IncomingWhatsAppMessage,
  SafeLogger,
  WhatsAppMessageReceiver,
} from '../src/contracts.js'

const message: IncomingWhatsAppMessage = {
  contractVersion: '1',
  channelMessageId: 'message-1',
  channelIdempotencyKey: 'stable-message-key',
  correlationId: '55555555-5555-4555-8555-555555555555',
  senderPhone: '5511999991234',
  conversationKind: 'individual',
  fromMe: false,
  receivedAtUtc: '2026-10-17T12:00:00.000Z',
  kind: 'text',
  text: 'O produto não liga.',
}

const logger: SafeLogger = {
  info: () => {},
  warn: () => {},
  error: () => {},
}

test('reports queued inbound messages and releases the listener on shutdown', async () => {
  let listener: ((incoming: IncomingWhatsAppMessage) => void) | undefined
  let unsubscribeCalled = false
  const receiver: WhatsAppMessageReceiver = {
    onIncomingMessage(value) {
      listener = value
      return () => {
        unsubscribeCalled = true
        listener = undefined
      }
    },
    onIncomingMedia() {
      return () => {}
    },
  }
  let resolveReported: (() => void) | undefined
  const reported = new Promise<void>((resolve) => {
    resolveReported = resolve
  })
  const received: IncomingWhatsAppMessage[] = []
  const api: ChannelApi = {
    async claim() {
      return null
    },
    async ingestMessage(value) {
      received.push(value)
      resolveReported?.()
    },
    async ingestMedia() {},
    async reportSessionState() {},
    async complete() {
      return true
    },
  }
  const controller = new AbortController()
  const worker = new IncomingMessageWorker(api, receiver, logger)
  const task = worker.run(controller.signal)

  listener?.(message)
  await reported
  controller.abort()
  await task

  assert.deepEqual(received, [message])
  assert.equal(unsubscribeCalled, true)
  assert.equal(listener, undefined)
})

test('schedules an early claim after a successful ingest, carrying the signal and a reason', async () => {
  let listener: ((incoming: IncomingWhatsAppMessage) => void) | undefined
  const receiver: WhatsAppMessageReceiver = {
    onIncomingMessage(value) {
      listener = value
      return () => {}
    },
    onIncomingMedia() {
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
  const worker = new IncomingMessageWorker(api, receiver, logger, earlyClaimTrigger)
  const task = worker.run(controller.signal)

  listener?.(message)
  // Wait until the ingest (and the subsequent early-claim trigger) completed.
  await new Promise((resolve) => setImmediate(resolve))
  controller.abort()
  await task

  assert.equal(scheduleCalls.length, 1)
  assert.equal(scheduleCalls[0]?.reason, 'inbound_text_ingested')
  assert.equal(scheduleCalls[0]?.signal, controller.signal)
})

test('a throwing early claim trigger is logged but does not stop the worker or lose the message', async () => {
  let listener: ((incoming: IncomingWhatsAppMessage) => void) | undefined
  const receiver: WhatsAppMessageReceiver = {
    onIncomingMessage(value) {
      listener = value
      return () => {}
    },
    onIncomingMedia() {
      return () => {}
    },
  }
  const received: IncomingWhatsAppMessage[] = []
  const api: ChannelApi = {
    async claim() {
      return null
    },
    async ingestMessage(value) {
      received.push(value)
    },
    async ingestMedia() {},
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
  const worker = new IncomingMessageWorker(api, receiver, capturingLogger, earlyClaimTrigger)
  const task = worker.run(controller.signal)

  listener?.(message)
  await new Promise((resolve) => setImmediate(resolve))
  controller.abort()
  await task

  assert.deepEqual(received, [message])
  assert.ok(loggedErrors.includes('channel_early_claim_trigger_failed'))
})
