import { chmod, mkdir } from 'node:fs/promises'
import { EventEmitter } from 'node:events'
import { createRequire } from 'node:module'
import makeWASocket, {
  DisconnectReason,
  useMultiFileAuthState,
  type AuthenticationState,
  type ConnectionState,
} from '@whiskeysockets/baileys'
import pino from 'pino'
import type { SafeLogger, WhatsAppMessageSender } from './contracts.js'

const qrcodeTerminal: typeof import('qrcode-terminal') =
  createRequire(import.meta.url)('qrcode-terminal')

export class SessionFatalError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'SessionFatalError'
  }
}

export interface BaileysSessionSocket {
  onConnectionUpdate(listener: (update: Partial<ConnectionState>) => void): () => void
  onCredentialsUpdate(listener: () => void): () => void
  sendMessage(
    recipientJid: string,
    text: string,
    messageId: string,
  ): Promise<{ key?: { id?: string | null } | null } | undefined>
  end(error?: Error): void
}

export interface BaileysSessionDependencies {
  loadAuthState?: typeof useMultiFileAuthState
  createSocket?: (auth: AuthenticationState) => BaileysSessionSocket
  wait?: (milliseconds: number, signal: AbortSignal) => Promise<void>
}

export class BaileysSession implements WhatsAppMessageSender {
  private readonly stateEvents = new EventEmitter()
  private readonly sessionPath: string
  private readonly printQr: boolean
  private readonly logger: SafeLogger
  private socket: BaileysSessionSocket | undefined
  private ready = false
  private fatalError: Error | undefined
  private started = false
  private readonly loadAuthState: typeof useMultiFileAuthState
  private readonly createSocket: (auth: AuthenticationState) => BaileysSessionSocket
  private readonly wait: (milliseconds: number, signal: AbortSignal) => Promise<void>

  constructor(
    sessionPath: string,
    printQr: boolean,
    logger: SafeLogger,
    dependencies: BaileysSessionDependencies = {},
  ) {
    this.sessionPath = sessionPath
    this.printQr = printQr
    this.logger = logger
    this.loadAuthState = dependencies.loadAuthState ?? useMultiFileAuthState
    this.createSocket = dependencies.createSocket ?? createBaileysSocket
    this.wait = dependencies.wait ?? delay
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
      socket.sendMessage(recipientJid, text, messageId),
      timeoutMs,
      signal,
    )

    if (result?.key?.id !== messageId) {
      throw new Error('Baileys não confirmou o identificador idempotente da mensagem.')
    }
  }

  private async reconnectLoop(signal: AbortSignal): Promise<void> {
    let consecutiveFailures = 0

    while (!signal.aborted) {
      let statusCode: number | undefined
      try {
        statusCode = await this.connectAndWaitForClose(signal, () => {
          consecutiveFailures = 0
        })
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
        await this.wait(retryInMs, signal)
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
      await this.wait(retryInMs, signal)
    }
  }

  private async connectAndWaitForClose(
    signal: AbortSignal,
    onConnected: () => void,
  ): Promise<number | undefined> {
    const auth = await this.loadAuthState(this.sessionPath)
    if (!auth.state.creds.registered && !this.printQr) {
      throw new SessionFatalError(
        'Não há sessão WhatsApp válida. Configure ChannelWorker__PrintQr=true para parear localmente.',
      )
    }

    const socket = this.createSocket(auth.state)
    this.socket = socket

    let credentialPersistenceFailed = false
    const pendingCredentialWrites = new Set<Promise<void>>()
    const statusCode = await new Promise<number | undefined>((resolve) => {
      let settled = false
      let removeConnectionListener = () => {}
      let removeCredentialsListener = () => {}

      const cleanup = () => {
        removeConnectionListener()
        removeCredentialsListener()
        signal.removeEventListener('abort', onAbort)
      }
      const finish = (status: number | undefined) => {
        if (settled) {
          return
        }

        settled = true
        cleanup()
        resolve(status)
      }
      const onConnectionUpdate = (update: Partial<ConnectionState>) => {
        if (update.qr !== undefined) {
          if (this.printQr) {
            this.logger.warn('whatsapp_pairing_qr_displayed')
            qrcodeTerminal.generate(update.qr, { small: true })
          } else {
            this.logger.warn('whatsapp_pairing_required_qr_output_disabled')
          }
        }

        if (update.connection === 'open') {
          this.ready = true
          onConnected()
          this.logger.info('whatsapp_session_connected')
          this.stateEvents.emit('ready')
        } else if (update.connection === 'close') {
          this.ready = false
          this.stateEvents.emit('disconnected')
          finish(getDisconnectStatusCode(update.lastDisconnect?.error))
        }
      }

      const onAbort = () => {
        socket.end(undefined)
        finish(undefined)
      }

      const onCredentialsUpdate = () => {
        let persistence: Promise<void>
        persistence = Promise.resolve()
          .then(() => auth.saveCreds())
          .catch((error: unknown) => {
            credentialPersistenceFailed = true
            this.logger.error('whatsapp_session_persistence_failed')
            socket.end(
              error instanceof Error
                ? error
                : new Error('Falha ao persistir sessão.'),
            )
            finish(undefined)
          })
          .finally(() => {
            pendingCredentialWrites.delete(persistence)
          })
        pendingCredentialWrites.add(persistence)
      }

      removeConnectionListener = socket.onConnectionUpdate(onConnectionUpdate)
      removeCredentialsListener = socket.onCredentialsUpdate(onCredentialsUpdate)
      signal.addEventListener('abort', onAbort, { once: true })
      if (signal.aborted) {
        onAbort()
      }
    })

    this.socket = undefined
    this.ready = false
    await Promise.all(pendingCredentialWrites)
    if (credentialPersistenceFailed) {
      throw new SessionFatalError('Não foi possível persistir as credenciais da sessão WhatsApp.')
    }

    return statusCode
  }
}

function createBaileysSocket(auth: AuthenticationState): BaileysSessionSocket {
  const socket = makeWASocket({
    auth,
    logger: pino({ level: 'silent' }),
  })

  return {
    onConnectionUpdate(listener) {
      socket.ev.on('connection.update', listener)
      return () => socket.ev.off('connection.update', listener)
    },
    onCredentialsUpdate(listener) {
      const handler = () => listener()
      socket.ev.on('creds.update', handler)
      return () => socket.ev.off('creds.update', handler)
    },
    sendMessage(recipientJid, text, messageId) {
      return socket.sendMessage(recipientJid, { text }, { messageId })
    },
    end(error) {
      socket.end(error)
    },
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
