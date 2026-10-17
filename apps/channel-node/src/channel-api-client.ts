import type { ChannelApi, ChannelOutcome, ClaimedChannelCommand } from './contracts.js'

const claimPath = '/api/internal/triagens/canal/claim'
const completePath = '/api/internal/triagens/canal/complete'

export class ChannelApiError extends Error {
  readonly operation: 'claim' | 'complete'
  readonly statusCode: number | null

  constructor(
    operation: 'claim' | 'complete',
    statusCode: number | null,
  ) {
    super(statusCode === null
      ? `Falha de transporte em ${operation}.`
      : `A API rejeitou ${operation} com HTTP ${statusCode}.`)
    this.name = 'ChannelApiError'
    this.operation = operation
    this.statusCode = statusCode
  }

  get isRetryable(): boolean {
    return this.statusCode === null || this.statusCode === 429 || (this.statusCode >= 500 && this.statusCode <= 599)
  }
}

export class ChannelApiClient implements ChannelApi {
  private readonly baseUrl: string
  private readonly claimToken: string
  private readonly completeToken: string
  private readonly fetcher: typeof fetch

  constructor(
    baseUrl: string,
    claimToken: string,
    completeToken: string,
    fetcher: typeof fetch = fetch,
  ) {
    this.baseUrl = baseUrl
    this.claimToken = claimToken
    this.completeToken = completeToken
    this.fetcher = fetcher
  }

  async claim(signal: AbortSignal): Promise<ClaimedChannelCommand | null> {
    let response: Response
    try {
      response = await this.fetcher(new URL(claimPath, this.baseUrl), {
        method: 'POST',
        headers: { authorization: `Bearer ${this.claimToken}`, accept: 'application/json' },
        signal,
      })
    } catch {
      throw new ChannelApiError('claim', null)
    }

    if (response.status === 204) {
      return null
    }

    if (response.status !== 200) {
      throw new ChannelApiError('claim', response.status)
    }

    let body: unknown
    try {
      body = await response.json()
    } catch {
      throw new Error('A API retornou JSON inválido para claim.')
    }

    return parseClaimedCommand(body)
  }

  async complete(
    command: ClaimedChannelCommand,
    outcome: ChannelOutcome,
    errorCode: string | null,
    errorDescription: string | null,
    signal: AbortSignal,
  ): Promise<boolean> {
    let response: Response
    try {
      response = await this.fetcher(new URL(completePath, this.baseUrl), {
        method: 'POST',
        headers: {
          authorization: `Bearer ${this.completeToken}`,
          'content-type': 'application/json',
          accept: 'application/json',
        },
        body: JSON.stringify({
          commandId: command.commandId,
          claimedAtUtc: command.claimedAtUtc,
          outcome,
          errorCode,
          errorDescription,
        }),
        signal,
      })
    } catch {
      throw new ChannelApiError('complete', null)
    }

    if (response.status === 409) {
      return false
    }

    if (response.status !== 200) {
      throw new ChannelApiError('complete', response.status)
    }

    await response.body?.cancel()
    return true
  }
}

function parseClaimedCommand(value: unknown): ClaimedChannelCommand {
  if (!isRecord(value)) {
    throw new Error('A API retornou um comando de canal inválido.')
  }

  const recipientPhone = value.recipientPhone
  if (
    (recipientPhone !== null && recipientPhone !== undefined && typeof recipientPhone !== 'string') ||
    !Number.isInteger(value.templateVersion) ||
    !Number.isInteger(value.attemptCount)
  ) {
    throw new Error('A API retornou metadados inválidos em claim.')
  }

  return {
    commandId: stringField(value, 'commandId'),
    triagemId: stringField(value, 'triagemId'),
    workOrderId: stringField(value, 'workOrderId'),
    templateId: stringField(value, 'templateId'),
    templateVersion: integerField(value, 'templateVersion'),
    idempotencyKey: stringField(value, 'idempotencyKey'),
    correlationId: stringField(value, 'correlationId'),
    attemptCount: integerField(value, 'attemptCount'),
    claimedAtUtc: stringField(value, 'claimedAtUtc'),
    claimExpiresAtUtc: stringField(value, 'claimExpiresAtUtc'),
    renderedContent: stringField(value, 'renderedContent'),
    recipientPhone: typeof recipientPhone === 'string' ? recipientPhone : null,
  }
}

function stringField(value: Record<string, unknown>, name: string): string {
  const field = value[name]
  if (typeof field !== 'string' || field === '') {
    throw new Error(`A API retornou o campo ${name} ausente ou inválido em claim.`)
  }

  return field
}

function integerField(value: Record<string, unknown>, name: string): number {
  const field = value[name]
  if (typeof field !== 'number' || !Number.isInteger(field)) {
    throw new Error(`A API retornou o campo ${name} ausente ou inválido em claim.`)
  }

  return field
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
