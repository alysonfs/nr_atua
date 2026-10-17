import { ChannelApiError } from './channel-api-client.js'
import type {
  ChannelApi,
  IncomingWhatsAppMessage,
  SafeLogger,
  WhatsAppMessageReceiver,
} from './contracts.js'

export class IncomingMessageWorker {
  private readonly api: ChannelApi
  private readonly receiver: WhatsAppMessageReceiver
  private readonly logger: SafeLogger

  constructor(api: ChannelApi, receiver: WhatsAppMessageReceiver, logger: SafeLogger) {
    this.api = api
    this.receiver = receiver
    this.logger = logger
  }

  async run(signal: AbortSignal): Promise<void> {
    const queue = new AsyncQueue<IncomingWhatsAppMessage>()
    const unsubscribe = this.receiver.onIncomingMessage((message) => queue.enqueue(message))

    try {
      while (!signal.aborted) {
        const message = await queue.dequeue(signal)
        if (message === undefined) {
          return
        }

        await this.reportWithRetry(message, signal)
      }
    } finally {
      unsubscribe()
      queue.close()
    }
  }

  private async reportWithRetry(message: IncomingWhatsAppMessage, signal: AbortSignal): Promise<void> {
    let consecutiveFailures = 0
    while (!signal.aborted) {
      try {
        await this.api.ingestMessage(message, signal)
        this.logger.info('channel_inbound_message_reported', {
          channelMessageId: message.channelMessageId,
          correlationId: message.correlationId,
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
          this.logger.error('channel_inbound_message_rejected', {
            statusCode: error.statusCode ?? 0,
            correlationId: message.correlationId,
          })
          throw error
        }

        consecutiveFailures += 1
        const retryInMs = Math.min(1_000 * 2 ** (consecutiveFailures - 1), 30_000)
        this.logger.warn('channel_inbound_message_retrying', {
          retryInMs,
          statusCode: error.statusCode ?? 0,
          correlationId: message.correlationId,
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
