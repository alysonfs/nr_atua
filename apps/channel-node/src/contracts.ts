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
  reportSessionState(event: WhatsAppSessionStateEvent, signal: AbortSignal): Promise<void>
}

/**
 * Estado de pareamento reportado pelo Channel à API (ADR-049) para que o
 * QR possa ser exibido na tela de Configurações do Office (OWNER/ADMIN),
 * já que a EC2 e seu EBS são destruídos a cada `make down`, perdendo a
 * sessão Baileys e exigindo novo pareamento. O conteúdo de `qr` nunca deve
 * ser logado — apenas enviado à API.
 */
export type WhatsAppSessionState = 'qr_pending' | 'connected' | 'disconnected' | 'logged_out'

export interface WhatsAppSessionStateEvent {
  state: WhatsAppSessionState
  /** Texto bruto do QR (não imagem) — presente apenas quando `state === 'qr_pending'`. */
  qr: string | null
  /** Telefone pareado — presente apenas quando `state === 'connected'`. */
  phoneNumber: string | null
  atUtc: string
}

export interface WhatsAppSessionStateReceiver {
  onSessionState(listener: (event: WhatsAppSessionStateEvent) => void): () => void
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

/**
 * Permite que um produtor de eventos inbound (texto ou mídia) acorde o
 * {@link ChannelWorker} antecipadamente após um ingest bem-sucedido, em vez
 * de esperar o próximo ciclo de polling — reduz a latência percebida entre
 * a resposta da IA virar comando na outbox e o envio pelo Channel. A
 * implementação nunca deve lançar: uma falha ao agendar o despertar
 * antecipado não pode derrubar o worker de origem.
 */
export interface EarlyClaimTrigger {
  scheduleEarlyClaim(signal: AbortSignal, reason: string): void
}
