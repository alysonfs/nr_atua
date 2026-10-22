import assert from 'node:assert/strict'
import test from 'node:test'
import { ChannelApiClient, ChannelApiError } from '../src/channel-api-client.js'
import type { ClaimedChannelCommand, IncomingWhatsAppMedia, IncomingWhatsAppMessage } from '../src/contracts.js'

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
  recipientPhone: '+5511999991234',
}

const inboundMessage: IncomingWhatsAppMessage = {
  contractVersion: '1',
  channelMessageId: 'message-123',
  channelIdempotencyKey: 'a'.repeat(64),
  correlationId: '55555555-5555-4555-8555-555555555555',
  senderPhone: '5511999991234',
  conversationKind: 'individual',
  fromMe: false,
  receivedAtUtc: '2026-10-17T12:00:00.000Z',
  kind: 'text',
  text: 'O produto não liga.',
}

test('claim uses the claim credential and deserializes the recipient snapshot', async () => {
  let requestUrl = ''
  let authorization = ''
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
    'media-only',
    async (input, init) => {
      requestUrl = String(input)
      authorization = new Headers(init?.headers).get('authorization') ?? ''
      return Response.json(command)
    },
  )

  const result = await client.claim(new AbortController().signal)

  assert.equal(requestUrl, 'http://localhost:5080/api/internal/triagens/canal/claim')
  assert.equal(authorization, 'Bearer claim-only')
  assert.deepEqual(result, command)
})

test('claim maps HTTP 204 to no available command', async () => {
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
    'media-only',
    async () => new Response(null, { status: 204 }),
  )

  assert.equal(await client.claim(new AbortController().signal), null)
})

test('complete uses its own credential and preserves the lease token', async () => {
  let authorization = ''
  let payload: Record<string, unknown> | undefined
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
    'media-only',
    async (_input, init) => {
      authorization = new Headers(init?.headers).get('authorization') ?? ''
      payload = JSON.parse(String(init?.body))
      return Response.json({
        commandId: command.commandId,
        status: 'CompletedSuccess',
        completedAtUtc: '2026-10-16T12:00:01Z',
      })
    },
  )

  assert.equal(
    await client.complete(command, 'Success', null, null, new AbortController().signal),
    true,
  )
  assert.equal(authorization, 'Bearer complete-only')
  assert.deepEqual(payload, {
    commandId: command.commandId,
    claimedAtUtc: command.claimedAtUtc,
    outcome: 'Success',
    errorCode: null,
    errorDescription: null,
  })
})

test('complete maps HTTP 409 to a lost lease without retrying the side effect', async () => {
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
    'media-only',
    async () => new Response(null, { status: 409 }),
  )

  assert.equal(
    await client.complete(command, 'Success', null, null, new AbortController().signal),
    false,
  )
})

test('ingestMessage uses the isolated credential and sends the stable event envelope', async () => {
  let requestUrl = ''
  let authorization = ''
  let payload: unknown
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
    'media-only',
    async (input, init) => {
      requestUrl = String(input)
      authorization = new Headers(init?.headers).get('authorization') ?? ''
      payload = JSON.parse(String(init?.body))
      return new Response(null, { status: 200 })
    },
  )

  await client.ingestMessage(inboundMessage, new AbortController().signal)

  assert.equal(requestUrl, 'http://localhost:5080/api/internal/triagens/canal/mensagens')
  assert.equal(authorization, 'Bearer ingest-only')
  assert.deepEqual(payload, inboundMessage)
})

const inboundMedia: IncomingWhatsAppMedia = {
  contractVersion: '1',
  channelMessageId: 'message-456',
  channelIdempotencyKey: 'b'.repeat(64),
  correlationId: '66666666-6666-4666-8666-666666666666',
  senderPhone: '5511999991234',
  conversationKind: 'individual',
  fromMe: false,
  receivedAtUtc: '2026-10-17T12:00:00.000Z',
  mediaKind: 'audio',
  contentType: 'audio/ogg',
  sizeBytes: 4,
  sha256: 'c'.repeat(64),
  isVoiceNote: true,
  durationSeconds: 3,
  caption: null,
  content: Buffer.from([1, 2, 3, 4]),
}

test('ingestMedia uses the isolated credential and sends metadata plus file as multipart', async () => {
  let requestUrl = ''
  let authorization = ''
  let form: FormData | undefined
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
    'media-only',
    async (input, init) => {
      requestUrl = String(input)
      authorization = new Headers(init?.headers).get('authorization') ?? ''
      form = init?.body as FormData
      return new Response(null, { status: 200 })
    },
  )

  await client.ingestMedia(inboundMedia, new AbortController().signal)

  assert.equal(requestUrl, 'http://localhost:5080/api/internal/triagens/canal/midias')
  assert.equal(authorization, 'Bearer media-only')
  assert.ok(form instanceof FormData)
  const metadata = JSON.parse(String(form?.get('metadata')))
  assert.equal(metadata.channelMessageId, inboundMedia.channelMessageId)
  assert.equal(metadata.mediaKind, 'audio')
  assert.equal(metadata.content, undefined)
  const file = form?.get('file') as Blob
  assert.equal(file.type, 'audio/ogg')
  assert.equal(file.size, 4)
})

test('ingestMedia maps a non-200 response to a typed, retryable-aware error', async () => {
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
    'media-only',
    async () => new Response(null, { status: 409 }),
  )

  await assert.rejects(
    client.ingestMedia(inboundMedia, new AbortController().signal),
    (error: unknown) => error instanceof ChannelApiError &&
      error.operation === 'ingestMedia' &&
      error.statusCode === 409 &&
      !error.isRetryable,
  )
})

test('API errors expose status but never retain a response body', async () => {
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
    'media-only',
    async () => new Response('sensitive server body', { status: 503 }),
  )

  await assert.rejects(
    client.claim(new AbortController().signal),
    (error: unknown) => error instanceof ChannelApiError &&
      error.statusCode === 503 &&
      error.isRetryable &&
      !error.message.includes('sensitive'),
  )
})

test('reportSessionState uses the dedicated credential and posts the raw session event', async () => {
  let requestUrl = ''
  let authorization = ''
  let body: unknown
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
    'media-only',
    async (input, init) => {
      requestUrl = String(input)
      authorization = new Headers(init?.headers).get('authorization') ?? ''
      body = JSON.parse(String(init?.body))
      return new Response(null, { status: 204 })
    },
    'session-report-only',
  )

  await client.reportSessionState(
    { state: 'qr_pending', qr: 'raw-qr-text', phoneNumber: null, atUtc: '2026-10-22T12:00:00.000Z' },
    new AbortController().signal,
  )

  assert.equal(requestUrl, 'http://localhost:5080/api/internal/channel/session')
  assert.equal(authorization, 'Bearer session-report-only')
  assert.deepEqual(body, {
    state: 'qr_pending',
    qr: 'raw-qr-text',
    phoneNumber: null,
    atUtc: '2026-10-22T12:00:00.000Z',
  })
})

test('reportSessionState throws without reaching the network when no credential is configured', async () => {
  let called = false
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
    'media-only',
    async () => {
      called = true
      return new Response(null, { status: 204 })
    },
  )

  await assert.rejects(
    client.reportSessionState(
      { state: 'connected', qr: null, phoneNumber: '5511999991234', atUtc: '2026-10-22T12:00:00.000Z' },
      new AbortController().signal,
    ),
    /SessionReportServiceToken/,
  )
  assert.equal(called, false)
})

test('reportSessionState maps a non-2xx response to a typed error', async () => {
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
    'media-only',
    async () => new Response(null, { status: 401 }),
    'session-report-only',
  )

  await assert.rejects(
    client.reportSessionState(
      { state: 'logged_out', qr: null, phoneNumber: null, atUtc: '2026-10-22T12:00:00.000Z' },
      new AbortController().signal,
    ),
    (error: unknown) => error instanceof ChannelApiError &&
      error.operation === 'reportSession' &&
      error.statusCode === 401 &&
      error.isAuthError,
  )
})
