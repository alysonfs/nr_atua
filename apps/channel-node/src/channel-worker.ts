import { ChannelApiError } from './channel-api-client.js'
import type {
  ChannelApi,
  ChannelOutcome,
  ClaimedChannelCommand,
  SafeLogger,
  WhatsAppMessageSender,
} from './contracts.js'
import { messageIdForIdempotencyKey, normalizeWhatsAppJid, PermanentChannelSendError } from './phone.js'
import { isAllowedTestRecipient } from './test-safeguard.js'

export interface ChannelWorkerOptions {
  pollingIntervalMs: number
  sendTimeoutMs: number
}

export type ChannelCycleResult = 'idle' | 'sent' | 'transient-failure' | 'permanent-failure' | 'lease-lost'

export class ChannelWorker {
  private readonly api: ChannelApi
  private readonly sender: WhatsAppMessageSender
  private readonly options: ChannelWorkerOptions
  private readonly logger: SafeLogger

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

  async run(signal: AbortSignal): Promise<void> {
    let consecutiveFailures = 0

    while (!signal.aborted) {
      try {
        const result = await this.runCycle(signal)
        consecutiveFailures = 0
        if (result === 'idle') {
          await delay(this.options.pollingIntervalMs, signal)
        }
      } catch (error) {
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
        await delay(backoffMs, signal)
      }
    }
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
