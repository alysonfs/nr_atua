import assert from 'node:assert/strict'
import test from 'node:test'
import { IncomingMessageWorker } from '../src/incoming-message-worker.js'
import type {
  ChannelApi,
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
