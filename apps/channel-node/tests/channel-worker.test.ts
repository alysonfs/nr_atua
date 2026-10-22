import assert from 'node:assert/strict'
import test from 'node:test'
import { ChannelWorker } from '../src/channel-worker.js'
import type {
  ChannelApi,
  ChannelOutcome,
  ClaimedChannelCommand,
  SafeLogger,
  WhatsAppMessageSender,
} from '../src/contracts.js'
import { messageIdForIdempotencyKey, normalizeWhatsAppJid } from '../src/phone.js'
import { FIXED_TEST_RECIPIENT_PHONE } from '../src/test-safeguard.js'

const command: ClaimedChannelCommand = {
  commandId: '11111111-1111-1111-1111-111111111111',
  triagemId: '22222222-2222-2222-2222-222222222222',
  workOrderId: '33333333-3333-3333-3333-333333333333',
  templateId: 'template-v1',
  templateVersion: 1,
  idempotencyKey: 'stable-idempotency-key',
  correlationId: '44444444-4444-4444-4444-444444444444',
  attemptCount: 1,
  claimedAtUtc: '2026-10-16T12:00:00Z',
  claimExpiresAtUtc: '2026-10-16T12:01:00Z',
  renderedContent: 'Texto aprovado',
  recipientPhone: FIXED_TEST_RECIPIENT_PHONE,
}

class FakeApi implements ChannelApi {
  claimed: ClaimedChannelCommand | null = command
  completeResult = true
  completions: Array<{ outcome: ChannelOutcome; errorCode: string | null }> = []

  async claim(): Promise<ClaimedChannelCommand | null> {
    return this.claimed
  }

  async ingestMessage(): Promise<void> {}

  async ingestMedia(): Promise<void> {}

  async reportSessionState(): Promise<void> {}

  async complete(
    _command: ClaimedChannelCommand,
    outcome: ChannelOutcome,
    errorCode: string | null,
  ): Promise<boolean> {
    this.completions.push({ outcome, errorCode })
    return this.completeResult
  }
}

class FakeSender implements WhatsAppMessageSender {
  sent: Array<{ jid: string; text: string; messageId: string; timeoutMs: number }> = []
  failure: Error | undefined

  async waitUntilReady(): Promise<void> {}

  async sendText(
    recipientJid: string,
    text: string,
    messageId: string,
    timeoutMs: number,
  ): Promise<void> {
    this.sent.push({ jid: recipientJid, text, messageId, timeoutMs })
    if (this.failure !== undefined) {
      throw this.failure
    }
  }
}

const logger: SafeLogger = {
  info: () => {},
  warn: () => {},
  error: () => {},
}

function createWorker(api: ChannelApi, sender: WhatsAppMessageSender): ChannelWorker {
  return new ChannelWorker(api, sender, { pollingIntervalMs: 15_000, sendTimeoutMs: 30_000 }, logger)
}

test('sends the approved content to the snapshotted phone and reports success', async () => {
  const api = new FakeApi()
  const sender = new FakeSender()
  const worker = createWorker(api, sender)

  const result = await worker.runCycle(new AbortController().signal)

  assert.equal(result, 'sent')
  assert.deepEqual(sender.sent, [{
    jid: normalizeWhatsAppJid(FIXED_TEST_RECIPIENT_PHONE),
    text: 'Texto aprovado',
    messageId: messageIdForIdempotencyKey(command.idempotencyKey),
    timeoutMs: 30_000,
  }])
  assert.deepEqual(api.completions, [{ outcome: 'Success', errorCode: null }])
})

test('a missing recipient is a permanent failure and is never sent', async () => {
  const api = new FakeApi()
  api.claimed = { ...command, recipientPhone: null }
  const sender = new FakeSender()
  const worker = createWorker(api, sender)

  const result = await worker.runCycle(new AbortController().signal)

  assert.equal(result, 'permanent-failure')
  assert.equal(sender.sent.length, 0)
  assert.deepEqual(api.completions, [{
    outcome: 'PermanentFailure',
    errorCode: 'RECIPIENT_PHONE_MISSING',
  }])
})

test('an invalid recipient is a permanent failure and is never sent', async () => {
  const api = new FakeApi()
  api.claimed = { ...command, recipientPhone: 'not a phone' }
  const sender = new FakeSender()
  const worker = createWorker(api, sender)

  const result = await worker.runCycle(new AbortController().signal)

  assert.equal(result, 'permanent-failure')
  assert.equal(sender.sent.length, 0)
  assert.deepEqual(api.completions, [{
    outcome: 'PermanentFailure',
    errorCode: 'RECIPIENT_PHONE_INVALID',
  }])
})

test('temporary E2E safeguard blocks any recipient other than the fixed test phone', async () => {
  const api = new FakeApi()
  api.claimed = { ...command, recipientPhone: '+5511999991234' }
  const sender = new FakeSender()
  const worker = createWorker(api, sender)

  const result = await worker.runCycle(new AbortController().signal)

  assert.equal(result, 'permanent-failure')
  assert.equal(sender.sent.length, 0)
  assert.deepEqual(api.completions, [{
    outcome: 'PermanentFailure',
    errorCode: 'TEST_RECIPIENT_NOT_ALLOWED',
  }])
})

test('a send timeout is transient and a retry keeps the stable message ID', async () => {
  const api = new FakeApi()
  const sender = new FakeSender()
  sender.failure = new Error('send timeout')
  const worker = createWorker(api, sender)
  const signal = new AbortController().signal

  assert.equal(await worker.runCycle(signal), 'transient-failure')
  sender.failure = undefined
  assert.equal(await worker.runCycle(signal), 'sent')

  assert.equal(api.completions[0]?.outcome, 'TransientFailure')
  assert.equal(api.completions[1]?.outcome, 'Success')
  assert.equal(sender.sent[0]?.messageId, sender.sent[1]?.messageId)
})

test('a lost lease does not report a completed send', async () => {
  const api = new FakeApi()
  api.completeResult = false
  const sender = new FakeSender()
  const worker = createWorker(api, sender)

  assert.equal(await worker.runCycle(new AbortController().signal), 'lease-lost')
  assert.equal(api.completions.length, 1)
})

test('the worker waits for WhatsApp readiness before claiming', async () => {
  let ready = false
  let claimed = false
  const api: ChannelApi = {
    async claim() {
      claimed = true
      return null
    },
    async ingestMessage() {},
    async ingestMedia() {},
    async reportSessionState() {},
    async complete() {
      return true
    },
  }
  const sender: WhatsAppMessageSender = {
    async waitUntilReady() {
      ready = true
    },
    async sendText() {},
  }
  const worker = createWorker(api, sender)

  await worker.runCycle(new AbortController().signal)

  assert.equal(ready, true)
  assert.equal(claimed, true)
})
