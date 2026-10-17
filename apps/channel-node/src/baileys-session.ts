import { chmod, mkdir } from 'node:fs/promises'
import { EventEmitter } from 'node:events'
import makeWASocket, {
  DisconnectReason,
  useMultiFileAuthState,
  type WASocket,
} from '@whiskeysockets/baileys'
import pino from 'pino'
import { generate as generateQr } from 'qrcode-terminal'
import type { SafeLogger, WhatsAppMessageSender } from './contracts.js'

export class SessionFatalError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'SessionFatalError'
  }
}

export class BaileysSession implements WhatsAppMessageSender {
  private readonly stateEvents = new EventEmitter()
  private readonly sessionPath: string
  private readonly printQr: boolean
  private readonly logger: SafeLogger
  private socket: WASocket | undefined
  private ready = false
  private fatalError: Error | undefined
  private started = false

  constructor(
    sessionPath: string,
    printQr: boolean,
    logger: SafeLogger,
  ) {
    this.sessionPath = sessionPath
    this.printQr = printQr
    this.logger = logger
  }

  async run(signal: AbortSignal): Promise<void> {
    if (this.started) {
      throw new SessionFatalError('A sessão Baileys já foi iniciada neste processo.')
    }

    this.started = true
    try {
      await mkdir(this.sessionPath, { recursive: true, mode: 0o700 })
      await chmod(this.sessionPath, 0o700)
      await this.reconnectLoop(signal)
    } catch (error) {
      if (signal.aborted) {
        return
      }

      const fatalError = error instanceof Error
        ? error
        : new SessionFatalError('Falha não recuperável na sessão WhatsApp.')
      this.fatalError = fatalError
      this.ready = false
      this.stateEvents.emit('fatal', fatalError)
      throw fatalError
    } finally {
      this.ready = false
      this.socket?.end(undefined)
      this.socket = undefined
    }
  }

  async waitUntilReady(signal: AbortSignal): Promise<void> {
    if (this.ready) {
      return
    }
    if (this.fatalError !== undefined) {
      throw this.fatalError
    }
    if (signal.aborted) {
      throw signal.reason ?? new Error('Encerramento solicitado.')
    }

    await new Promise<void>((resolve, reject) => {
      const cleanup = () => {
        this.stateEvents.off('ready', onReady)
        this.stateEvents.off('fatal', onFatal)
        signal.removeEventListener('abort', onAbort)
      }
      const onReady = () => {
        cleanup()
        resolve()
      }
      const onFatal = (error: Error) => {
        cleanup()
        reject(error)
      }
      const onAbort = () => {
        cleanup()
        reject(signal.reason ?? new Error('Encerramento solicitado.'))
      }

      this.stateEvents.once('ready', onReady)
      this.stateEvents.once('fatal', onFatal)
      signal.addEventListener('abort', onAbort, { once: true })

      if (this.ready) {
        onReady()
      } else if (this.fatalError !== undefined) {
        onFatal(this.fatalError)
      }
    })
  }

  async sendText(
    recipientJid: string,
    text: string,
    messageId: string,
    timeoutMs: number,
    signal: AbortSignal,
  ): Promise<void> {
    const socket = this.socket
    if (!this.ready || socket === undefined) {
      throw new Error('A sessão WhatsApp não está conectada.')
    }

    const result = await withTimeout(
      socket.sendMessage(recipientJid, { text }, { messageId }),
      timeoutMs,
      signal,
    )

    if (result?.key.id !== messageId) {
      throw new Error('Baileys não confirmou o identificador idempotente da mensagem.')
    }
  }

  private async reconnectLoop(signal: AbortSignal): Promise<void> {
    let consecutiveFailures = 0

    while (!signal.aborted) {
      let statusCode: number | undefined
      try {
        statusCode = await this.connectAndWaitForClose(signal)
      } catch (error) {
        if (signal.aborted) {
          return
        }
        if (error instanceof SessionFatalError) {
          throw error
        }

        consecutiveFailures += 1
        const retryInMs = Math.min(1_000 * 2 ** (consecutiveFailures - 1), 30_000)
        this.logger.warn('whatsapp_connection_failed_retrying', {
          errorType: error instanceof Error ? error.name : 'UnknownError',
          retryInMs,
        })
        await delay(retryInMs, signal)
        continue
      }

      if (signal.aborted) {
        return
      }

      if (isFatalDisconnect(statusCode)) {
        throw new SessionFatalError('A sessão WhatsApp exige intervenção para autenticar novamente.')
      }

      consecutiveFailures += 1
      const retryInMs = Math.min(1_000 * 2 ** (consecutiveFailures - 1), 30_000)
      this.logger.warn('whatsapp_disconnected_retrying', {
        statusCode: statusCode ?? 0,
        retryInMs,
      })
      await delay(retryInMs, signal)
    }
  }

  private async connectAndWaitForClose(signal: AbortSignal): Promise<number | undefined> {
    const auth = await useMultiFileAuthState(this.sessionPath)
    if (!auth.state.creds.registered && !this.printQr) {
      throw new SessionFatalError(
        'Não há sessão WhatsApp válida. Configure ChannelWorker__PrintQr=true para parear localmente.',
      )
    }

    const socket = makeWASocket({
      auth: auth.state,
      logger: pino({ level: 'silent' }),
    })
    this.socket = socket

    let credentialPersistenceFailed = false
    const statusCode = await new Promise<number | undefined>((resolve) => {
      const onConnectionUpdate = (update: { connection?: string; qr?: string; lastDisconnect?: { error?: unknown } }) => {
        if (update.qr !== undefined) {
          if (this.printQr) {
            this.logger.warn('whatsapp_pairing_qr_displayed')
            generateQr(update.qr, { small: true })
          } else {
            this.logger.warn('whatsapp_pairing_required_qr_output_disabled')
          }
        }

        if (update.connection === 'open') {
          this.ready = true
          this.logger.info('whatsapp_session_connected')
          this.stateEvents.emit('ready')
        } else if (update.connection === 'close') {
          this.ready = false
          this.stateEvents.emit('disconnected')
          cleanup()
          resolve(getDisconnectStatusCode(update.lastDisconnect?.error))
        }
      }

      const onAbort = () => {
        cleanup()
        socket.end(undefined)
        resolve(undefined)
      }

      const onCredentialsUpdate = () => {
        void auth.saveCreds().catch(() => {
          credentialPersistenceFailed = true
          this.logger.error('whatsapp_session_persistence_failed')
          socket.end(new Error('Falha ao persistir sessão.'))
        })
      }

      const cleanup = () => {
        socket.ev.off('connection.update', onConnectionUpdate)
        socket.ev.off('creds.update', onCredentialsUpdate)
        signal.removeEventListener('abort', onAbort)
      }

      socket.ev.on('connection.update', onConnectionUpdate)
      socket.ev.on('creds.update', onCredentialsUpdate)
      signal.addEventListener('abort', onAbort, { once: true })
      if (signal.aborted) {
        onAbort()
      }
    })

    this.socket = undefined
    this.ready = false
    if (credentialPersistenceFailed) {
      throw new SessionFatalError('Não foi possível persistir as credenciais da sessão WhatsApp.')
    }

    return statusCode
  }
}

function getDisconnectStatusCode(error: unknown): number | undefined {
  if (typeof error !== 'object' || error === null || !('output' in error)) {
    return undefined
  }

  const output = error.output
  if (typeof output !== 'object' || output === null || !('statusCode' in output)) {
    return undefined
  }

  return typeof output.statusCode === 'number' ? output.statusCode : undefined
}

function isFatalDisconnect(statusCode: number | undefined): boolean {
  return statusCode === DisconnectReason.loggedOut ||
    statusCode === DisconnectReason.badSession ||
    statusCode === DisconnectReason.connectionReplaced ||
    statusCode === DisconnectReason.forbidden
}

async function withTimeout<T>(
  operation: Promise<T>,
  timeoutMs: number,
  signal: AbortSignal,
): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined
  let onAbort: (() => void) | undefined

  try {
    return await Promise.race([
      operation,
      new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error('Envio WhatsApp expirou.')), timeoutMs)
        onAbort = () => reject(signal.reason ?? new Error('Encerramento solicitado.'))
        signal.addEventListener('abort', onAbort, { once: true })
        if (signal.aborted) {
          onAbort()
        }
      }),
    ])
  } finally {
    if (timer !== undefined) {
      clearTimeout(timer)
    }
    if (onAbort !== undefined) {
      signal.removeEventListener('abort', onAbort)
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
