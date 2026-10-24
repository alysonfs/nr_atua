import { jidNormalizedUser, type proto, type WAMessage } from '@whiskeysockets/baileys'
import type { SafeLogger } from './contracts.js'

export const OUTBOUND_RETRY_TTL_MS = 60 * 60 * 1_000
export const MAX_OUTBOUND_RETRY_MESSAGES = 256

interface CachedMessage {
  recipients: Set<string>
  expiresAt: number
  payload: Promise<proto.IMessage | undefined>
  settle: (message: proto.IMessage | undefined) => void
  timer: ReturnType<typeof setTimeout>
}

export class OutboundMessageCache {
  private readonly messages = new Map<string, CachedMessage>()
  private readonly logger: SafeLogger
  private recovery: ((key: proto.IMessageKey) => Promise<proto.IMessage | undefined>) | undefined

  constructor(logger: SafeLogger) {
    this.logger = logger
  }

  /** Fallback used only when the original is not in memory (e.g. after a process restart). */
  setRecovery(recovery: (key: proto.IMessageKey) => Promise<proto.IMessage | undefined>): void {
    this.recovery = recovery
  }

  async send(
    recipientJid: string,
    messageId: string,
    aliases: readonly string[],
    sendMessage: () => Promise<WAMessage | undefined>,
  ): Promise<WAMessage | undefined> {
    const previous = this.messages.get(messageId)
    const expiresAt = previous !== undefined && previous.expiresAt > Date.now()
      ? previous.expiresAt
      : Date.now() + OUTBOUND_RETRY_TTL_MS
    if (previous !== undefined) {
      this.remove(messageId, previous)
    }
    if (this.messages.size >= MAX_OUTBOUND_RETRY_MESSAGES) {
      const oldest = this.messages.entries().next().value
      if (oldest !== undefined) {
        this.remove(oldest[0], oldest[1])
        this.logger.warn('whatsapp_retry_cache_evicted', { reason: 'capacity' })
      }
    }

    let settle!: CachedMessage['settle']
    const payload = new Promise<proto.IMessage | undefined>((resolve) => { settle = resolve })
    const entry: CachedMessage = {
      recipients: new Set([recipientJid, ...aliases].map(jidNormalizedUser)),
      expiresAt,
      payload,
      settle,
      timer: setTimeout(() => {
        this.remove(messageId, entry)
        this.logger.info('whatsapp_retry_cache_evicted', { reason: 'expired' })
      }, expiresAt - Date.now()),
    }
    entry.timer.unref()
    this.messages.set(messageId, entry)

    try {
      const result = await sendMessage()
      if (result?.key.id !== messageId || result.message === undefined || result.message === null) {
        throw new Error('Baileys não disponibilizou a mensagem original para retransmissão.')
      }
      entry.settle(result.message)
      return result
    } catch (error) {
      this.remove(messageId, entry)
      this.logger.error('whatsapp_retry_cache_send_failed', {
        errorType: error instanceof Error ? error.name : 'UnknownError',
      })
      throw error
    }
  }

  readonly getMessage = async (key: proto.IMessageKey): Promise<proto.IMessage | undefined> => {
    const entry = key.id === undefined || key.id === null ? undefined : this.messages.get(key.id)
    if (entry === undefined || key.id === undefined || key.id === null) {
      return this.recover(key)
    }
    if (key.fromMe === false || !entry.recipients.has(jidNormalizedUser(key.remoteJid ?? ''))) {
      return this.unavailable('recipient_mismatch')
    }
    if (Date.now() >= entry.expiresAt) {
      this.remove(key.id, entry)
      return this.unavailable('expired')
    }

    // A receipt may arrive before sendMessage resolves; keep the original payload, not reconstructed text.
    const message = await entry.payload
    if (message === undefined || Date.now() >= entry.expiresAt || this.messages.get(key.id) !== entry) {
      return this.unavailable('no_longer_available')
    }
    this.logger.info('whatsapp_retry_message_available', { messageId: key.id })
    return message
  }

  clear(): void {
    for (const [messageId, entry] of this.messages) {
      this.remove(messageId, entry)
    }
  }

  private remove(messageId: string, entry: CachedMessage): void {
    clearTimeout(entry.timer)
    entry.settle(undefined)
    if (this.messages.get(messageId) === entry) {
      this.messages.delete(messageId)
    }
  }

  private async recover(key: proto.IMessageKey): Promise<proto.IMessage | undefined> {
    if (this.recovery === undefined || key.fromMe === false || typeof key.id !== 'string') {
      return this.unavailable('not_cached')
    }
    try {
      const message = await this.recovery(key)
      if (message === undefined) {
        return this.unavailable('not_recoverable')
      }
      this.logger.info('whatsapp_retry_message_recovered', { messageId: key.id })
      return message
    } catch (error) {
      this.logger.warn('whatsapp_retry_recovery_failed', {
        errorType: error instanceof Error ? error.name : 'UnknownError',
      })
      return undefined
    }
  }

  private unavailable(reason: string): undefined {
    this.logger.warn('whatsapp_retry_message_unavailable', { reason })
    return undefined
  }
}
