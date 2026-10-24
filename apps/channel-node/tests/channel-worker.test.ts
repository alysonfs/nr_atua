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

function createWorker(
  api: ChannelApi,
  sender: WhatsAppMessageSender,
  overrides: Partial<{ pollingIntervalMs: number; sendTimeoutMs: number; earlyClaimDelaysMs: number[] }> = {},
): ChannelWorker {
  return new ChannelWorker(
    api,
    sender,
    {
      pollingIntervalMs: 15_000,
      sendTimeoutMs: 30_000,
      earlyClaimDelaysMs: [0, 3_000, 6_000, 10_000],
      ...overrides,
    },
    logger,
  )
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

test('temporary E2E safeguard blocks any recipient outside the authorized test phones', async () => {
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

test('temporary E2E safeguard allows the second authorized test phone', async () => {
  const api = new FakeApi()
  api.claimed = { ...command, recipientPhone: '+5584991050065' }
  const sender = new FakeSender()
  const worker = createWorker(api, sender)

  await worker.runCycle(new AbortController().signal)

  assert.equal(sender.sent.length, 1)
  assert.equal(sender.sent[0]?.jid, normalizeWhatsAppJid('+5584991050065'))
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

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setImmediate(resolve))
}

test('scheduleEarlyClaim wakes an idling worker immediately, ahead of the polling interval', async (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] })

  let claimCalls = 0
  const api: ChannelApi = {
    async claim() {
      claimCalls += 1
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
    async waitUntilReady() {},
    async sendText() {},
  }
  const worker = createWorker(api, sender, { pollingIntervalMs: 15_000, earlyClaimDelaysMs: [0] })
  const controller = new AbortController()
  const task = worker.run(controller.signal)

  await flushMicrotasks()
  assert.equal(claimCalls, 1, 'the first cycle claims immediately on startup')

  worker.scheduleEarlyClaim(controller.signal, 'inbound_text_ingested')
  // Only the 0ms early-claim timer fires here — nowhere near the 15s polling interval.
  t.mock.timers.tick(0)
  await flushMicrotasks()
  assert.equal(claimCalls, 2, 'the early-claim kick triggers a new cycle without waiting for the poll interval')

  // Advancing a little more (still far from 15s) must not trigger another claim on its own.
  t.mock.timers.tick(1_000)
  await flushMicrotasks()
  assert.equal(claimCalls, 2, 'no extra claim happens without a new kick or the full polling interval')

  // Completing the remaining interval proves normal polling still works as a recovery mechanism.
  t.mock.timers.tick(14_000)
  await flushMicrotasks()
  assert.equal(claimCalls, 3, 'normal polling still recovers after the configured interval')

  controller.abort()
  await task
})

test('kicks received while a cycle is running coalesce into a single extra cycle (no overlap)', async (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] })

  const claimQueue: Array<ClaimedChannelCommand | null> = [command, null, null]
  let claimCalls = 0
  let releaseSend: (() => void) | undefined
  let sendCalls = 0
  const api: ChannelApi = {
    async claim() {
      const next = claimQueue[claimCalls] ?? null
      claimCalls += 1
      return next
    },
    async ingestMessage() {},
    async ingestMedia() {},
    async reportSessionState() {},
    async complete() {
      return true
    },
  }
  const sender: WhatsAppMessageSender = {
    async waitUntilReady() {},
    async sendText() {
      sendCalls += 1
      await new Promise<void>((resolve) => {
        releaseSend = resolve
      })
    },
  }
  const worker = createWorker(api, sender, { pollingIntervalMs: 15_000, earlyClaimDelaysMs: [0] })
  const controller = new AbortController()
  const task = worker.run(controller.signal)

  await flushMicrotasks()
  assert.equal(claimCalls, 1, 'the first cycle claimed the pending command and is now sending it')
  assert.equal(sendCalls, 1)
  assert.equal(typeof releaseSend, 'function', 'the cycle is paused mid-send, simulating a slow in-flight cycle')

  // Three ingests arrive while the cycle above is still running — all three
  // must coalesce into exactly one extra cycle once the current one ends.
  worker.scheduleEarlyClaim(controller.signal, 'inbound_text_ingested')
  t.mock.timers.tick(0)
  await flushMicrotasks()
  worker.scheduleEarlyClaim(controller.signal, 'inbound_media_ingested')
  t.mock.timers.tick(0)
  await flushMicrotasks()
  worker.scheduleEarlyClaim(controller.signal, 'inbound_text_ingested')
  t.mock.timers.tick(0)
  await flushMicrotasks()

  assert.equal(claimCalls, 1, 'no new cycle starts while the current one is still in progress')
  assert.equal(sendCalls, 1, 'the in-progress cycle was not duplicated by the pending kicks')

  releaseSend?.()
  await flushMicrotasks()
  await flushMicrotasks()

  assert.equal(claimCalls, 2, 'exactly one extra cycle ran immediately after the in-progress one finished')
  assert.equal(sendCalls, 1, 'the extra cycle claimed null (idle) and never called sendText again')

  // Normal polling still recovers afterwards.
  t.mock.timers.tick(15_000)
  await flushMicrotasks()
  assert.equal(claimCalls, 3, 'the worker keeps polling normally after the coalesced kick cycle')

  controller.abort()
  await task
})

test('normal polling keeps recovering even when no early claim is ever scheduled', async (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] })

  let claimCalls = 0
  const api: ChannelApi = {
    async claim() {
      claimCalls += 1
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
    async waitUntilReady() {},
    async sendText() {},
  }
  const worker = createWorker(api, sender, { pollingIntervalMs: 15_000, earlyClaimDelaysMs: [0, 3_000, 6_000, 10_000] })
  const controller = new AbortController()
  const task = worker.run(controller.signal)

  await flushMicrotasks()
  assert.equal(claimCalls, 1)

  t.mock.timers.tick(15_000)
  await flushMicrotasks()
  assert.equal(claimCalls, 2)

  t.mock.timers.tick(15_000)
  await flushMicrotasks()
  assert.equal(claimCalls, 3)

  controller.abort()
  await task
})

test('a failure while scheduling an early claim is logged and does not stop the worker', async (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] })

  let claimCalls = 0
  const api: ChannelApi = {
    async claim() {
      claimCalls += 1
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
    async waitUntilReady() {},
    async sendText() {},
  }
  const loggedErrors: Array<{ event: string; fields: Record<string, string | number | boolean> }> = []
  const capturingLogger: SafeLogger = {
    info: () => {},
    warn: () => {},
    error: (event, fields = {}) => {
      loggedErrors.push({ event, fields })
    },
  }
  // earlyClaimDelaysMs is intentionally broken (not iterable) to force scheduleEarlyClaim to throw internally.
  const worker = new ChannelWorker(
    api,
    sender,
    {
      pollingIntervalMs: 15_000,
      sendTimeoutMs: 30_000,
      earlyClaimDelaysMs: null as unknown as number[],
    },
    capturingLogger,
  )
  const controller = new AbortController()
  const task = worker.run(controller.signal)

  await flushMicrotasks()
  assert.equal(claimCalls, 1)

  assert.doesNotThrow(() => worker.scheduleEarlyClaim(controller.signal, 'inbound_text_ingested'))
  await flushMicrotasks()

  assert.ok(
    loggedErrors.some((entry) => entry.event === 'channel_early_claim_schedule_failed'),
    'the scheduling failure is logged with a structured event',
  )

  // The worker keeps polling normally despite the early-claim failure.
  t.mock.timers.tick(15_000)
  await flushMicrotasks()
  assert.equal(claimCalls, 2, 'normal polling is unaffected by the early-claim scheduling failure')

  controller.abort()
  await task
})
