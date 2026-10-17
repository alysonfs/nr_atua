export type ChannelOutcome = 'Success' | 'TransientFailure' | 'PermanentFailure'

export interface ClaimedChannelCommand {
  commandId: string
  triagemId: string
  workOrderId: string
  templateId: string
  templateVersion: number
  idempotencyKey: string
  correlationId: string
  attemptCount: number
  claimedAtUtc: string
  claimExpiresAtUtc: string
  renderedContent: string
  recipientPhone: string | null
}

export interface ChannelApi {
  claim(signal: AbortSignal): Promise<ClaimedChannelCommand | null>
  complete(
    command: ClaimedChannelCommand,
    outcome: ChannelOutcome,
    errorCode: string | null,
    errorDescription: string | null,
    signal: AbortSignal,
  ): Promise<boolean>
}

export interface WhatsAppMessageSender {
  waitUntilReady(signal: AbortSignal): Promise<void>
  sendText(
    recipientJid: string,
    text: string,
    messageId: string,
    timeoutMs: number,
    signal: AbortSignal,
  ): Promise<void>
}

export interface SafeLogger {
  info(event: string, fields?: Record<string, string | number | boolean>): void
  warn(event: string, fields?: Record<string, string | number | boolean>): void
  error(event: string, fields?: Record<string, string | number | boolean>): void
}
