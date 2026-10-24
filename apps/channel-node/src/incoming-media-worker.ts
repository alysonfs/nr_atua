import { ChannelApiError } from './channel-api-client.js'
import type {
  ChannelApi,
  EarlyClaimTrigger,
  IncomingWhatsAppMedia,
  SafeLogger,
  WhatsAppMessageReceiver,
} from './contracts.js'

/** Capacidade padrão da fila inbound de mídia (R1) quando não configurada via `ChannelWorker__MediaQueueCapacity`. */
export const DEFAULT_MEDIA_QUEUE_CAPACITY = 20

/** Trigger nulo usado quando nenhum {@link EarlyClaimTrigger} é injetado — mantém o worker funcional sem o recurso de busca antecipada. */
const noopEarlyClaimTrigger: EarlyClaimTrigger = {
  scheduleEarlyClaim() {},
}

/**
 * Espelha {@link IncomingMessageWorker} para o evento de mídia — fila
 * assíncrona + retentativa com backoff exponencial, mas usando
 * {@link ChannelApi.ingestMedia} e a assinatura <c>onIncomingMedia</c> do
 * receiver. Mantido como implementação própria (não compartilhada) para
 * preservar o caminho de texto intocado.
 *
 * R1 (docs/implementation/RISCOS-triagem-midia-pendentes.md, resolvido em
 * 2026-10-22): a fila tem capacidade limitada — mídia excedente é
 * descartada com log em vez de crescer sem limite.
 *
 * R2 (mesmo documento): uma rejeição permanente (não retentável) da API é
 * registrada e o worker segue consumindo a fila — não propaga a exceção
 * para fora de {@link run}, exceto quando a credencial em si é inválida
 * (401/403), caso fatal de configuração que deve interromper o processo.
 */
export class IncomingMediaWorker {
  private readonly api: ChannelApi
  private readonly receiver: WhatsAppMessageReceiver
  private readonly logger: SafeLogger
  private readonly queueCapacity: number
  private readonly earlyClaimTrigger: EarlyClaimTrigger

  constructor(
    api: ChannelApi,
    receiver: WhatsAppMessageReceiver,
    logger: SafeLogger,
    queueCapacity: number = DEFAULT_MEDIA_QUEUE_CAPACITY,
    earlyClaimTrigger: EarlyClaimTrigger = noopEarlyClaimTrigger,
  ) {
    this.api = api
    this.receiver = receiver
    this.logger = logger
    this.queueCapacity = queueCapacity
    this.earlyClaimTrigger = earlyClaimTrigger
  }

  async run(signal: AbortSignal): Promise<void> {
    const queue = new AsyncQueue<IncomingWhatsAppMedia>(this.queueCapacity)
    const unsubscribe = this.receiver.onIncomingMedia((media) => {
      const accepted = queue.enqueue(media)
      if (!accepted) {
        this.logger.error('channel_inbound_media_queue_full_discarded', {
          channelMessageId: media.channelMessageId,
          correlationId: media.correlationId,
          mediaKind: media.mediaKind,
          sizeBytes: media.sizeBytes,
          queueCapacity: this.queueCapacity,
        })
      }
    })

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
        this.triggerEarlyClaim(media, signal)
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
          if (error.isAuthError) {
            // Erro de credencial/configuração — fatal, diferente da rejeição
            // permanente por evento (R2): não há benefício em seguir
            // consumindo a fila com uma credencial inválida.
            throw error
          }
          return
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

  /**
   * Agenda buscas antecipadas de comandos (claim) após o ingest bem-sucedido
   * da mídia — nunca lança: uma falha no agendamento não pode derrubar este
   * worker (o polling normal do {@link ChannelWorker} segue como
   * recuperação).
   */
  private triggerEarlyClaim(media: IncomingWhatsAppMedia, signal: AbortSignal): void {
    try {
      this.earlyClaimTrigger.scheduleEarlyClaim(signal, 'inbound_media_ingested')
    } catch (error) {
      this.logger.error('channel_early_claim_trigger_failed', {
        channelMessageId: media.channelMessageId,
        correlationId: media.correlationId,
        errorType: error instanceof Error ? error.name : 'UnknownError',
      })
    }
  }
}

class AsyncQueue<T> {
  private readonly items: T[] = []
  private readonly waiters = new Set<() => void>()
  private closed = false
  private readonly capacity: number

  constructor(capacity: number = Infinity) {
    this.capacity = capacity
  }

  /** Retorna `false` (sem lançar) quando a fila está cheia ou fechada — o item é descartado pelo chamador. */
  enqueue(item: T): boolean {
    if (this.closed) {
      return false
    }
    if (this.items.length >= this.capacity) {
      return false
    }

    this.items.push(item)
    this.waiters.values().next().value?.()
    return true
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
