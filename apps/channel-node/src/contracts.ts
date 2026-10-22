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
  ingestMessage(message: IncomingWhatsAppMessage, signal: AbortSignal): Promise<void>
  ingestMedia(media: IncomingWhatsAppMedia, signal: AbortSignal): Promise<void>
  complete(
    command: ClaimedChannelCommand,
    outcome: ChannelOutcome,
    errorCode: string | null,
    errorDescription: string | null,
    signal: AbortSignal,
  ): Promise<boolean>
}

export interface IncomingWhatsAppMessage {
  contractVersion: '1'
  channelMessageId: string
  channelIdempotencyKey: string
  correlationId: string
  senderPhone: string
  conversationKind: 'individual' | 'group'
  fromMe: boolean
  receivedAtUtc: string
  kind: 'text' | 'empty'
  text: string | null
}

export type IncomingWhatsAppMediaKind = 'audio' | 'image' | 'video' | 'document'

/**
 * Evento de mídia recebido pelo canal WhatsApp — campos de envelope
 * compartilhados com {@link IncomingWhatsAppMessage} mais os metadados e o
 * binário da mídia. Mídia é sempre um evento único: nunca também relatado
 * como {@link IncomingWhatsAppMessage} (decisão do PO, 2026-10-21).
 */
export interface IncomingWhatsAppMedia {
  contractVersion: '1'
  channelMessageId: string
  channelIdempotencyKey: string
  correlationId: string
  senderPhone: string
  conversationKind: 'individual' | 'group'
  fromMe: boolean
  receivedAtUtc: string
  mediaKind: IncomingWhatsAppMediaKind
  contentType: string
  sizeBytes: number
  sha256: string
  isVoiceNote: boolean
  durationSeconds: number | null
  caption: string | null
  /** Binário já baixado e descriptografado do WhatsApp — nunca persistido localmente, apenas enviado ao endpoint de ingestão. */
  content: Buffer
}

export interface WhatsAppMessageReceiver {
  onIncomingMessage(listener: (message: IncomingWhatsAppMessage) => void): () => void
  onIncomingMedia(listener: (media: IncomingWhatsAppMedia) => void): () => void
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
