import { ChannelApiError } from './channel-api-client.js'
import type {
  ChannelApi,
  IncomingWhatsAppMedia,
  SafeLogger,
  WhatsAppMessageReceiver,
} from './contracts.js'

/**
 * Espelha {@link IncomingMessageWorker} para o evento de mídia — fila
 * assíncrona + retentativa com backoff exponencial, mas usando
 * {@link ChannelApi.ingestMedia} e a assinatura <c>onIncomingMedia</c> do
 * receiver. Mantido como implementação própria (não compartilhada) para
 * preservar o caminho de texto intocado.
 */
export class IncomingMediaWorker {
  private readonly api: ChannelApi
  private readonly receiver: WhatsAppMessageReceiver
  private readonly logger: SafeLogger

  constructor(api: ChannelApi, receiver: WhatsAppMessageReceiver, logger: SafeLogger) {
    this.api = api
    this.receiver = receiver
    this.logger = logger
  }

  async run(signal: AbortSignal): Promise<void> {
    const queue = new AsyncQueue<IncomingWhatsAppMedia>()
    const unsubscribe = this.receiver.onIncomingMedia((media) => queue.enqueue(media))

    try {
      while (!signal.aborted) {
        const media = await queue.dequeue(signal)
        if (media === undefined) {
          return
        }

        await this.reportWithRetry(media, signal)
      }
    } finally {
      unsubscribe()
      queue.close()
    }
  }

  private async reportWithRetry(media: IncomingWhatsAppMedia, signal: AbortSignal): Promise<void> {
    let consecutiveFailures = 0
    while (!signal.aborted) {
      try {
        await this.api.ingestMedia(media, signal)
        this.logger.info('channel_inbound_media_reported', {
          channelMessageId: media.channelMessageId,
          correlationId: media.correlationId,
          mediaKind: media.mediaKind,
        })
        return
      } catch (error) {
        if (signal.aborted) {
          return
        }
        if (!(error instanceof ChannelApiError)) {
          throw error
        }
        if (!error.isRetryable) {
          this.logger.error('channel_inbound_media_rejected', {
            statusCode: error.statusCode ?? 0,
            correlationId: media.correlationId,
          })
          throw error
        }

        consecutiveFailures += 1
        const retryInMs = Math.min(1_000 * 2 ** (consecutiveFailures - 1), 30_000)
        this.logger.warn('channel_inbound_media_retrying', {
          retryInMs,
          statusCode: error.statusCode ?? 0,
          correlationId: media.correlationId,
        })
        await delay(retryInMs, signal)
      }
    }
  }
}

class AsyncQueue<T> {
  private readonly items: T[] = []
  private readonly waiters = new Set<() => void>()
  private closed = false

  enqueue(item: T): void {
    if (this.closed) {
      return
    }

    this.items.push(item)
    this.waiters.values().next().value?.()
  }

  async dequeue(signal: AbortSignal): Promise<T | undefined> {
    while (this.items.length === 0 && !this.closed && !signal.aborted) {
      await new Promise<void>((resolve) => {
        const wake = () => {
          this.waiters.delete(wake)
          signal.removeEventListener('abort', wake)
          resolve()
        }
        this.waiters.add(wake)
        signal.addEventListener('abort', wake, { once: true })
      })
    }

    return this.items.shift()
  }

  close(): void {
    this.closed = true
    for (const wake of this.waiters) {
      wake()
    }
  }
}

function delay(milliseconds: number, signal: AbortSignal): Promise<void> {
  if (signal.aborted) {
    return Promise.resolve()
  }

  return new Promise((resolve) => {
    const timer = setTimeout(() => {
      signal.removeEventListener('abort', onAbort)
      resolve()
    }, milliseconds)
    const onAbort = () => {
      clearTimeout(timer)
      signal.removeEventListener('abort', onAbort)
      resolve()
    }
    signal.addEventListener('abort', onAbort, { once: true })
  })
}
