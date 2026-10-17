import assert from 'node:assert/strict'
import test from 'node:test'
import { ChannelApiClient, ChannelApiError } from './channel-api-client.js'
import type { ClaimedChannelCommand, IncomingWhatsAppMessage } from './contracts.js'

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

test('API errors expose status but never retain a response body', async () => {
  const client = new ChannelApiClient(
    'http://localhost:5080',
    'claim-only',
    'complete-only',
    'ingest-only',
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
