import { ChannelApiError } from './channel-api-client.js'
import type {
  ChannelApi,
  ChannelOutcome,
  ClaimedChannelCommand,
  EarlyClaimTrigger,
  SafeLogger,
  WhatsAppMessageSender,
} from './contracts.js'
import { messageIdForIdempotencyKey, normalizeWhatsAppJid, PermanentChannelSendError } from './phone.js'
import { isAllowedTestRecipient } from './test-safeguard.js'

export interface ChannelWorkerOptions {
  pollingIntervalMs: number
  sendTimeoutMs: number
  /**
   * Atrasos (em ms, a partir de 0 e crescentes) em que uma busca antecipada
   * de comandos é disparada após {@link ChannelWorker.scheduleEarlyClaim}
   * ser chamado — ex.: `[0, 3_000, 6_000, 10_000]`. O polling normal
   * (`pollingIntervalMs`) continua funcionando como mecanismo de
   * recuperação, independentemente deste agendamento.
   */
  earlyClaimDelaysMs: number[]
}

export type ChannelCycleResult = 'idle' | 'sent' | 'transient-failure' | 'permanent-failure' | 'lease-lost'

export class ChannelWorker implements EarlyClaimTrigger {
  private readonly api: ChannelApi
  private readonly sender: WhatsAppMessageSender
  private readonly options: ChannelWorkerOptions
  private readonly logger: SafeLogger

  /** `true` enquanto um ciclo (claim → envio → complete) está em execução. */
  private cycleInProgress = false
  /** Marca que um novo ciclo deve rodar assim que o atual terminar, sem esperar o intervalo de polling. */
  private pendingKick = false
  /** Resolvers da espera atual (idle ou backoff) — acordados por um kick antecipado. */
  private readonly waitResolvers = new Set<() => void>()
  /** Timers agendados por {@link scheduleEarlyClaim}, limpos no shutdown. */
  private readonly scheduledTimers = new Set<ReturnType<typeof setTimeout>>()

  constructor(
    api: ChannelApi,
    sender: WhatsAppMessageSender,
    options: ChannelWorkerOptions,
    logger: SafeLogger,
  ) {
    this.api = api
    this.sender = sender
    this.options = options
    this.logger = logger
  }

  /**
   * Agenda tentativas antecipadas de claim/envio nos atrasos configurados
   * (`earlyClaimDelaysMs`) a partir de agora. Chamado após o ingest
   * bem-sucedido de uma mensagem inbound (texto ou mídia). Nunca lança —
   * uma falha aqui não pode derrubar o worker de origem (ingest).
   */
  scheduleEarlyClaim(signal: AbortSignal, reason: string): void {
    try {
      if (signal.aborted) {
        return
      }

      for (const delayMs of this.options.earlyClaimDelaysMs) {
        const timer = setTimeout(() => {
          this.scheduledTimers.delete(timer)
          this.kick(reason)
        }, delayMs)
        if (typeof timer.unref === 'function') {
          timer.unref()
        }
        this.scheduledTimers.add(timer)
      }

      this.logger.info('channel_early_claim_scheduled', {
        reason,
        attempts: this.options.earlyClaimDelaysMs.length,
      })
    } catch (error) {
      this.logger.error('channel_early_claim_schedule_failed', {
        reason,
        errorType: error instanceof Error ? error.name : 'UnknownError',
      })
    }
  }

  /** Acorda uma espera em andamento ou marca para rodar novamente assim que o ciclo atual terminar. */
  private kick(reason: string): void {
    try {
      if (this.cycleInProgress) {
        this.pendingKick = true
        return
      }

      if (this.waitResolvers.size === 0) {
        // Nada esperando no momento (ciclo nem em execução, nem em espera) —
        // o próprio loop seguirá seu curso normal; não há o que acordar.
        return
      }

      this.logger.info('channel_early_claim_triggered', { reason })
      for (const resolve of this.waitResolvers) {
        resolve()
      }
    } catch (error) {
      this.logger.error('channel_early_claim_kick_failed', {
        reason,
        errorType: error instanceof Error ? error.name : 'UnknownError',
      })
    }
  }

  private clearScheduledTimers(): void {
    for (const timer of this.scheduledTimers) {
      clearTimeout(timer)
    }
    this.scheduledTimers.clear()
  }

  async run(signal: AbortSignal): Promise<void> {
    let consecutiveFailures = 0
    const onAbort = () => this.clearScheduledTimers()
    signal.addEventListener('abort', onAbort, { once: true })

    try {
      while (!signal.aborted) {
        try {
          this.cycleInProgress = true
          const result = await this.runCycle(signal)
          this.cycleInProgress = false
          consecutiveFailures = 0

          if (this.pendingKick) {
            this.pendingKick = false
            continue
          }

          if (result === 'idle') {
            await this.waitInterruptibly(this.options.pollingIntervalMs, signal)
          }
        } catch (error) {
          this.cycleInProgress = false
          if (signal.aborted) {
            return
          }

          if (error instanceof ChannelApiError && !error.isRetryable) {
            this.logger.error('channel_api_rejected_request', {
              operation: error.operation,
              statusCode: error.statusCode ?? 0,
            })
            throw error
          }

          consecutiveFailures += 1
          const backoffMs = Math.min(1_000 * 2 ** (consecutiveFailures - 1), 30_000)
          this.logger.error('channel_cycle_failed_retrying', {
            retryInMs: backoffMs,
            errorType: error instanceof Error ? error.name : 'UnknownError',
          })

          if (this.pendingKick) {
            this.pendingKick = false
            continue
          }

          await this.waitInterruptibly(backoffMs, signal)
        }
      }
    } finally {
      signal.removeEventListener('abort', onAbort)
      this.clearScheduledTimers()
    }
  }

  /** Espera `milliseconds`, mas pode ser interrompida antecipadamente por um kick de {@link scheduleEarlyClaim} ou pelo abort do `signal`. */
  private waitInterruptibly(milliseconds: number, signal: AbortSignal): Promise<void> {
    if (signal.aborted) {
      return Promise.resolve()
    }

    return new Promise((resolve) => {
      let settled = false
      const finish = () => {
        if (settled) {
          return
        }
        settled = true
        clearTimeout(timer)
        this.waitResolvers.delete(finish)
        signal.removeEventListener('abort', finish)
        resolve()
      }
      const timer = setTimeout(finish, milliseconds)
      this.waitResolvers.add(finish)
      signal.addEventListener('abort', finish, { once: true })
    })
  }

  async runCycle(signal: AbortSignal): Promise<ChannelCycleResult> {
    await this.sender.waitUntilReady(signal)
    const command = await this.api.claim(signal)
    if (command === null) {
      return 'idle'
    }

    const result = await this.sendCommand(command, signal)
    const completed = await this.api.complete(
      command,
      result.outcome,
      result.errorCode,
      result.errorDescription,
      signal,
    )

    if (!completed) {
      this.logger.warn('channel_command_lease_lost', {
        commandId: command.commandId,
        correlationId: command.correlationId,
      })
      return 'lease-lost'
    }

    this.logger.info('channel_command_completed', {
      commandId: command.commandId,
      correlationId: command.correlationId,
      outcome: result.outcome,
    })

    if (result.outcome === 'Success') {
      return 'sent'
    }

    return result.outcome === 'PermanentFailure' ? 'permanent-failure' : 'transient-failure'
  }

  private async sendCommand(
    command: ClaimedChannelCommand,
    signal: AbortSignal,
  ): Promise<{
    outcome: ChannelOutcome
    errorCode: string | null
    errorDescription: string | null
  }> {
    if (command.recipientPhone === null || command.recipientPhone.trim() === '') {
      return permanentFailure('RECIPIENT_PHONE_MISSING', 'A OS não possui telefone de destino.')
    }

    try {
      const recipientJid = normalizeWhatsAppJid(command.recipientPhone)
      if (!isAllowedTestRecipient(recipientJid)) {
        return permanentFailure(
          'TEST_RECIPIENT_NOT_ALLOWED',
          'Envio bloqueado pela trava temporária do E2E: destinatário fora da lista permitida.',
        )
      }

      await this.sender.sendText(
        recipientJid,
        command.renderedContent,
        messageIdForIdempotencyKey(command.idempotencyKey),
        this.options.sendTimeoutMs,
        signal,
      )
      return { outcome: 'Success', errorCode: null, errorDescription: null }
    } catch (error) {
      if (signal.aborted) {
        throw error
      }

      if (error instanceof PermanentChannelSendError) {
        return permanentFailure(error.code, error.message)
      }

      return {
        outcome: 'TransientFailure',
        errorCode: 'WHATSAPP_SEND_FAILED',
        errorDescription: 'O envio não foi confirmado; uma retentativa usará a mesma chave idempotente.',
      }
    }
  }
}

function permanentFailure(
  errorCode: string,
  errorDescription: string,
): { outcome: 'PermanentFailure'; errorCode: string; errorDescription: string } {
  return { outcome: 'PermanentFailure', errorCode, errorDescription }
}
