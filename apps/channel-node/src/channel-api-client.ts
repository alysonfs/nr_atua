import type {
  ChannelApi,
  ChannelOutcome,
  ClaimedChannelCommand,
  IncomingWhatsAppMedia,
  IncomingWhatsAppMessage,
  WhatsAppSessionStateEvent,
} from './contracts.js'

const claimPath = '/api/internal/triagens/canal/claim'
const completePath = '/api/internal/triagens/canal/complete'
const messageIngestPath = '/api/internal/triagens/canal/mensagens'
const mediaIngestPath = '/api/internal/triagens/canal/midias'
const sessionReportPath = '/api/internal/channel/session'
const retransmissionPath = '/api/internal/triagens/canal/retransmissao'

export class ChannelApiError extends Error {
  readonly operation: 'claim' | 'complete' | 'ingest' | 'ingestMedia' | 'reportSession'
  readonly statusCode: number | null

  constructor(
    operation: 'claim' | 'complete' | 'ingest' | 'ingestMedia' | 'reportSession',
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
    return this.statusCode === null ||
      this.statusCode === 408 ||
      this.statusCode === 429 ||
      (this.statusCode >= 500 && this.statusCode <= 599)
  }

  /**
   * Erro de credencial/configuração (R2 — ver
   * docs/implementation/RISCOS-triagem-midia-pendentes.md): diferente de
   * uma rejeição permanente por evento, indica que o próprio processo está
   * mal configurado e deve ser tratado como fatal pelos consumidores.
   */
  get isAuthError(): boolean {
    return this.statusCode === 401 || this.statusCode === 403
  }
}

export class ChannelApiClient implements ChannelApi {
  private readonly baseUrl: string
  private readonly claimToken: string
  private readonly completeToken: string
  private readonly messageIngestToken: string
  private readonly mediaIngestToken: string
  private readonly sessionReportToken: string | undefined
  private readonly fetcher: typeof fetch

  constructor(
    baseUrl: string,
    claimToken: string,
    completeToken: string,
    messageIngestToken: string,
    mediaIngestToken: string,
    fetcher: typeof fetch = fetch,
    sessionReportToken?: string,
  ) {
    this.baseUrl = baseUrl
    this.claimToken = claimToken
    this.completeToken = completeToken
    this.messageIngestToken = messageIngestToken
    this.mediaIngestToken = mediaIngestToken
    this.fetcher = fetcher
    this.sessionReportToken = sessionReportToken
  }

  async ingestMessage(message: IncomingWhatsAppMessage, signal: AbortSignal): Promise<void> {
    let response: Response
    try {
      response = await this.fetcher(new URL(messageIngestPath, this.baseUrl), {
        method: 'POST',
        headers: {
          authorization: `Bearer ${this.messageIngestToken}`,
          'content-type': 'application/json',
          accept: 'application/json',
        },
        body: JSON.stringify(message),
        signal,
      })
    } catch {
      throw new ChannelApiError('ingest', null)
    }

    if (response.status !== 200) {
      throw new ChannelApiError('ingest', response.status)
    }

    await response.body?.cancel()
  }

  async ingestMedia(media: IncomingWhatsAppMedia, signal: AbortSignal): Promise<void> {
    const { content, ...metadata } = media
    const form = new FormData()
    form.set('metadata', JSON.stringify(metadata))
    form.set('file', new Blob([content], { type: media.contentType }), 'inbound-media.bin')

    let response: Response
    try {
      response = await this.fetcher(new URL(mediaIngestPath, this.baseUrl), {
        method: 'POST',
        headers: {
          authorization: `Bearer ${this.mediaIngestToken}`,
          accept: 'application/json',
        },
        body: form,
        signal,
      })
    } catch {
      throw new ChannelApiError('ingestMedia', null)
    }

    if (response.status !== 200) {
      throw new ChannelApiError('ingestMedia', response.status)
    }

    await response.body?.cancel()
  }

  /** Returns null when the message is unknown or outside the API retransmission window. */
  async recoverRetransmission(
    messageId: string,
    signal: AbortSignal,
  ): Promise<{ text: string; recipientPhone: string } | null> {
    let response: Response
    try {
      response = await this.fetcher(new URL(retransmissionPath, this.baseUrl), {
        method: 'POST',
        headers: {
          authorization: `Bearer ${this.claimToken}`,
          'content-type': 'application/json',
          accept: 'application/json',
        },
        body: JSON.stringify({ messageId }),
        signal,
      })
    } catch {
      throw new ChannelApiError('claim', null)
    }

    if (response.status === 404) {
      await response.body?.cancel()
      return null
    }
    if (response.status !== 200) {
      throw new ChannelApiError('claim', response.status)
    }

    const body: unknown = await response.json()
    if (!isRecord(body) || typeof body.text !== 'string' || body.text === '' ||
      typeof body.recipientPhone !== 'string' || body.recipientPhone === '') {
      throw new Error('A API retornou uma retransmissão inválida.')
    }
    return { text: body.text, recipientPhone: body.recipientPhone }
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

  async reportSessionState(event: WhatsAppSessionStateEvent, signal: AbortSignal): Promise<void> {
    if (this.sessionReportToken === undefined) {
      throw new Error('ChannelWorker__SessionReportServiceToken não configurado.')
    }

    let response: Response
    try {
      response = await this.fetcher(new URL(sessionReportPath, this.baseUrl), {
        method: 'POST',
        headers: {
          authorization: `Bearer ${this.sessionReportToken}`,
          'content-type': 'application/json',
          accept: 'application/json',
        },
        body: JSON.stringify(event),
        signal,
      })
    } catch {
      throw new ChannelApiError('reportSession', null)
    }

    if (response.status !== 200 && response.status !== 204) {
      throw new ChannelApiError('reportSession', response.status)
    }

    await response.body?.cancel()
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
