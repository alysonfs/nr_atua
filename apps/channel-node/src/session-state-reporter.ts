import type { ChannelApi, SafeLogger, WhatsAppSessionStateEvent, WhatsAppSessionStateReceiver } from './contracts.js'

/**
 * Repassa à API o estado de pareamento do Baileys (ADR-049) — QR pendente,
 * conectado, desconectado ou deslogado — para que o Office possa exibi-lo
 * na tela de Configurações (OWNER/ADMIN), já que a EC2 e seu EBS são
 * destruídos a cada `make down`.
 *
 * Diferente de {@link IncomingMediaWorker} e {@link IncomingMessageWorker},
 * esta é uma função de observabilidade auxiliar — uma falha ao reportar o
 * estado nunca deve interromper o processamento de texto/mídia nem encerrar
 * o processo do Channel. Falhas (inclusive de autenticação da credencial
 * dedicada) são registradas e o worker segue consumindo os próximos
 * eventos. Nunca loga o conteúdo de `event.qr`.
 */
export class SessionStateReporter {
  private readonly api: ChannelApi
  private readonly receiver: WhatsAppSessionStateReceiver
  private readonly logger: SafeLogger
  private readonly enabled: boolean

  constructor(
    api: ChannelApi,
    receiver: WhatsAppSessionStateReceiver,
    logger: SafeLogger,
    enabled: boolean,
  ) {
    this.api = api
    this.receiver = receiver
    this.logger = logger
    this.enabled = enabled
  }

  async run(signal: AbortSignal): Promise<void> {
    if (!this.enabled) {
      this.logger.warn('channel_session_report_disabled')
      return
    }

    const queue = new SessionStateQueue()
    const unsubscribe = this.receiver.onSessionState((event) => queue.enqueue(event))

    try {
      while (!signal.aborted) {
        const event = await queue.dequeue(signal)
        if (event === undefined) {
          return
        }

        await this.reportBestEffort(event, signal)
      }
    } finally {
      unsubscribe()
      queue.close()
    }
  }

  private async reportBestEffort(event: WhatsAppSessionStateEvent, signal: AbortSignal): Promise<void> {
    try {
      await this.api.reportSessionState(event, signal)
      this.logger.info('channel_session_state_reported', { state: event.state })
    } catch (error) {
      if (signal.aborted) {
        return
      }

      this.logger.warn('channel_session_state_report_failed', {
        state: event.state,
        errorType: error instanceof Error ? error.name : 'UnknownError',
      })
    }
  }
}

class SessionStateQueue {
  private readonly items: WhatsAppSessionStateEvent[] = []
  private readonly waiters = new Set<() => void>()
  private closed = false

  enqueue(item: WhatsAppSessionStateEvent): void {
    if (this.closed) {
      return
    }

    this.items.push(item)
    this.waiters.values().next().value?.()
  }

  async dequeue(signal: AbortSignal): Promise<WhatsAppSessionStateEvent | undefined> {
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
