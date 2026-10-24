import { chmod, mkdir } from 'node:fs/promises'
import { EventEmitter } from 'node:events'
import { createHash, randomUUID } from 'node:crypto'
import { createRequire } from 'node:module'
import makeWASocket, {
  DisconnectReason,
  downloadMediaMessage,
  useMultiFileAuthState,
  type AuthenticationState,
  type BinaryNode,
  type ConnectionState,
  type WAMessage,
  type UserFacingSocketConfig,
  jidNormalizedUser,
  normalizeMessageContent,
  proto,
  type WAMessageUpdate,
} from '@whiskeysockets/baileys'
import pino from 'pino'
import type {
  IncomingWhatsAppMedia,
  IncomingWhatsAppMediaKind,
  IncomingWhatsAppMessage,
  SafeLogger,
  WhatsAppMessageReceiver,
  WhatsAppMessageSender,
  WhatsAppSessionState,
  WhatsAppSessionStateEvent,
  WhatsAppSessionStateReceiver,
} from './contracts.js'
import { normalizeWhatsAppJid, PermanentChannelSendError } from './phone.js'
import { collectBufferWithLimit, MAX_INBOUND_MEDIA_BYTES, MediaTooLargeError } from './media-download-limit.js'
import { OutboundMessageCache } from './outbound-message-cache.js'

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
  onMessagesUpsert(listener: (messages: WAMessage[], type: string) => void): () => void
  onMessageAck(listener: (messageId: string, errorCode?: string) => void): () => void
  resolveRecipient(recipientJid: string): Promise<{ jid: string; aliases: readonly string[] }>
  sendMessage(
    recipientJid: string,
    text: string,
    messageId: string,
    recipientAliases?: readonly string[],
  ): Promise<{ key?: { id?: string | null } | null } | undefined>
  end(error?: Error): void
}

export interface BaileysSessionDependencies {
  loadAuthState?: typeof useMultiFileAuthState
  createSocket?: (auth: AuthenticationState) => BaileysSessionSocket
  wait?: (milliseconds: number, signal: AbortSignal) => Promise<void>
  /** Baixa e descriptografa o binário de uma mensagem de mídia — injetável para testes com buffers sintéticos. */
  downloadMedia?: (message: WAMessage) => Promise<Buffer>
  /** Instante atual — injetável para tornar `atUtc` determinístico nos testes. */
  now?: () => Date
  /**
   * Busca na API o texto de uma mensagem enviada na última hora, apenas para
   * atender pedido de retransmissão quando o original não está mais em memória.
   */
  recoverOutboundText?: (messageId: string) => Promise<{ text: string; recipientPhone: string } | null>
}

export class BaileysSession implements WhatsAppMessageSender, WhatsAppMessageReceiver, WhatsAppSessionStateReceiver {
  private readonly stateEvents = new EventEmitter()
  private readonly incomingMessageListeners = new Set<(message: IncomingWhatsAppMessage) => void>()
  private readonly incomingMediaListeners = new Set<(media: IncomingWhatsAppMedia) => void>()
  private readonly sessionStateListeners = new Set<(event: WhatsAppSessionStateEvent) => void>()
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
  private readonly downloadMedia: (message: WAMessage) => Promise<Buffer>
  private readonly now: () => Date
  private readonly outboundMessages: OutboundMessageCache

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
    this.outboundMessages = new OutboundMessageCache(logger)
    const recoverOutboundText = dependencies.recoverOutboundText
    if (recoverOutboundText !== undefined) {
      this.outboundMessages.setRecovery((key) => this.recoverOutboundMessage(key, recoverOutboundText))
    }
    this.createSocket = dependencies.createSocket ??
      ((auth) => createBaileysSocket(auth, logger, this.outboundMessages))
    this.wait = dependencies.wait ?? delay
    this.downloadMedia = dependencies.downloadMedia ?? defaultDownloadMedia
    this.now = dependencies.now ?? (() => new Date())
  }

  onSessionState(listener: (event: WhatsAppSessionStateEvent) => void): () => void {
    this.sessionStateListeners.add(listener)
    return () => this.sessionStateListeners.delete(listener)
  }

  private async recoverOutboundMessage(
    key: proto.IMessageKey,
    recoverOutboundText: NonNullable<BaileysSessionDependencies['recoverOutboundText']>,
  ): Promise<proto.IMessage | undefined> {
    if (typeof key.id !== 'string' || !/^[A-F0-9]{32}$/.test(key.id) || !key.remoteJid) {
      return undefined
    }
    const recovered = await recoverOutboundText(key.id)
    if (recovered === null) {
      return undefined
    }

    // Only answer the retry for the original recipient (phone JID or its LID alias).
    const remoteJid = jidNormalizedUser(key.remoteJid)
    const phoneJid = normalizeWhatsAppJid(recovered.recipientPhone)
    let allowed = remoteJid === phoneJid
    if (!allowed && remoteJid.endsWith('@lid') && this.socket !== undefined) {
      const recipient = await this.socket.resolveRecipient(phoneJid)
      allowed = [recipient.jid, ...recipient.aliases].map((jid) => jidNormalizedUser(jid)).includes(remoteJid)
    }
    if (!allowed) {
      this.logger.warn('whatsapp_retry_recovery_recipient_mismatch')
      return undefined
    }
    return proto.Message.fromObject({ extendedTextMessage: { text: recovered.text } })
  }

  /** Nunca loga o conteúdo de `event.qr` — apenas repassa aos listeners. */
  private emitSessionState(state: WhatsAppSessionState, qr: string | null, phoneNumber: string | null): void {
    const event: WhatsAppSessionStateEvent = {
      state,
      qr,
      phoneNumber,
      atUtc: this.now().toISOString(),
    }
    for (const listener of this.sessionStateListeners) {
      listener(event)
    }
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
      this.outboundMessages.clear()
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

  onIncomingMessage(listener: (message: IncomingWhatsAppMessage) => void): () => void {
    this.incomingMessageListeners.add(listener)
    return () => this.incomingMessageListeners.delete(listener)
  }

  onIncomingMedia(listener: (media: IncomingWhatsAppMedia) => void): () => void {
    this.incomingMediaListeners.add(listener)
    return () => this.incomingMediaListeners.delete(listener)
  }

  /**
   * Baixa o binário e entrega o evento de mídia aos listeners. Executado de
   * forma assíncrona e isolada por mensagem (não bloqueia o processamento
   * das demais mensagens do mesmo upsert). Uma falha de download é
   * registrada e a mensagem é descartada — não há fallback para texto, pois
   * mídia é sempre um evento único.
   */
  private async deliverIncomingMedia(
    message: WAMessage,
    envelope: ParsedEnvelope,
    metadata: IncomingMediaMetadata,
  ): Promise<void> {
    let content: Buffer
    try {
      content = await this.downloadMedia(message)
    } catch (error) {
      if (error instanceof MediaTooLargeError) {
        this.logger.error('whatsapp_inbound_media_too_large_discarded', {
          channelMessageId: envelope.messageId,
          correlationId: envelope.correlationId,
          limitBytes: error.limitBytes,
        })
        return
      }

      this.logger.error('whatsapp_inbound_media_download_failed', {
        channelMessageId: envelope.messageId,
        correlationId: envelope.correlationId,
        errorType: error instanceof Error ? error.name : 'UnknownError',
      })
      return
    }

    const media: IncomingWhatsAppMedia = {
      contractVersion: '1',
      channelMessageId: envelope.messageId,
      channelIdempotencyKey: envelope.channelIdempotencyKey,
      correlationId: envelope.correlationId,
      senderPhone: envelope.senderPhone,
      conversationKind: 'individual',
      fromMe: envelope.fromMe,
      receivedAtUtc: envelope.receivedAtUtc,
      mediaKind: metadata.mediaKind,
      contentType: metadata.contentType,
      sizeBytes: content.length,
      sha256: createHash('sha256').update(content).digest('hex'),
      isVoiceNote: metadata.isVoiceNote,
      durationSeconds: metadata.durationSeconds,
      caption: metadata.caption,
      content,
    }

    this.logger.info('whatsapp_inbound_media_downloaded', {
      channelMessageId: envelope.messageId,
      correlationId: envelope.correlationId,
      mediaKind: metadata.mediaKind,
      sizeBytes: content.length,
    })
    for (const listener of this.incomingMediaListeners) {
      listener(media)
    }
  }

  async sendText(
    recipientJid: string,
    text: string,
    messageId: string,
    timeoutMs: number,
    signal: AbortSignal,
  ): Promise<void> {
    const socket = this.socket
    signal.throwIfAborted()
    if (!this.ready || socket === undefined) {
      throw new Error('A sessão WhatsApp não está conectada.')
    }

    let removeAckListener = () => {}
    let removeDisconnectListener = () => {}
    let sendingAllowed = true
    const acknowledgement = new Promise<void>((resolve, reject) => {
      removeAckListener = socket.onMessageAck((ackId, errorCode) => {
        if (ackId !== messageId) {
          return
        }
        if (errorCode !== undefined) {
          this.logger.error('whatsapp_message_rejected', {
            messageId,
            errorCode: /^\d{1,4}$/.test(errorCode) ? errorCode : 'UNKNOWN',
          })
          reject(new Error('O servidor WhatsApp rejeitou a mensagem.'))
        } else {
          resolve()
        }
      })
      const onDisconnected = () => reject(new Error('A sessão desconectou antes da confirmação.'))
      this.stateEvents.once('disconnected', onDisconnected)
      removeDisconnectListener = () => this.stateEvents.off('disconnected', onDisconnected)
    })

    try {
      await withTimeout(
        Promise.all([
          acknowledgement,
          (async () => {
            const recipient = await socket.resolveRecipient(recipientJid)
            signal.throwIfAborted()
            if (!sendingAllowed || !this.ready || this.socket !== socket) {
              throw new Error('O envio expirou ou a sessão WhatsApp mudou durante a consulta.')
            }
            this.logger.info('whatsapp_recipient_resolved', {
              messageId,
              canonicalJidChanged: recipient.jid !== recipientJid,
            })
            const result = await socket.sendMessage(recipient.jid, text, messageId, recipient.aliases)
            if (result?.key?.id !== messageId) {
              throw new Error('Baileys não confirmou o identificador idempotente da mensagem.')
            }
          })(),
        ]),
        timeoutMs,
        signal,
      )
      this.logger.info('whatsapp_message_server_ack', { messageId })
    } catch (error) {
      this.logger.warn('whatsapp_message_send_unconfirmed', {
        messageId,
        errorType: error instanceof Error ? error.name : 'UnknownError',
      })
      throw error
    } finally {
      sendingAllowed = false
      removeAckListener()
      removeDisconnectListener()
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
        this.logger.error('whatsapp_disconnected_fatal', { statusCode: statusCode ?? 0 })
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
    // QR pairing leaves `registered` false; only pairing-code logins set it, so `me` also proves a paired session.
    const hasPairedSession = auth.state.creds.registered || Boolean(auth.state.creds.me?.id)
    // Pairing is possible when the QR is printed locally or delivered to the Office (ADR-049).
    if (!hasPairedSession && !this.printQr && this.sessionStateListeners.size === 0) {
      this.logger.error('whatsapp_session_unpaired_fatal', { hasPairedSession, printQr: this.printQr })
      throw new SessionFatalError(
        'Não há sessão WhatsApp válida. Configure ChannelWorker__PrintQr=true ou ChannelWorker__SessionReportServiceToken para parear.',
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
      let removeMessagesListener = () => {}
      const cleanup = () => {
        removeConnectionListener()
        removeCredentialsListener()
        removeMessagesListener()
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
          this.emitSessionState('qr_pending', update.qr, null)
        }

        if (update.connection === 'open') {
          this.ready = true
          onConnected()
          this.logger.info('whatsapp_session_connected')
          this.stateEvents.emit('ready')
          this.emitSessionState('connected', null, extractPhoneFromJid(auth.state.creds.me?.id))
        } else if (update.connection === 'close') {
          this.ready = false
          this.stateEvents.emit('disconnected')
          const statusCode = getDisconnectStatusCode(update.lastDisconnect?.error)
          this.emitSessionState(statusCode === DisconnectReason.loggedOut ? 'logged_out' : 'disconnected', null, null)
          finish(statusCode)
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

      const onMessagesUpsert = (messages: WAMessage[], type: string) => {
        if (type !== 'notify') {
          this.logger.info('whatsapp_inbound_upsert_ignored', {
            upsertType: type === 'append' ? 'append' : 'other',
            messageCount: messages.length,
            reason: 'non_notify',
          })
          return
        }

        this.logger.info('whatsapp_inbound_upsert_received', {
          messageCount: messages.length,
        })

        for (const message of messages) {
          const envelopeResult = parseEnvelope(message)
          if (envelopeResult.envelope === null) {
            this.logger.info('whatsapp_inbound_message_ignored', {
              reason: envelopeResult.reason,
              remoteJidKind: getJidKind(message.key.remoteJid ?? ''),
            })
            continue
          }

          const mediaResult = extractMediaMetadata(message)
          if (mediaResult.media !== null) {
            this.logger.info('whatsapp_inbound_media_received', {
              channelMessageId: envelopeResult.envelope.messageId,
              correlationId: envelopeResult.envelope.correlationId,
              mediaKind: mediaResult.media.mediaKind,
            })
            void this.deliverIncomingMedia(message, envelopeResult.envelope, mediaResult.media)
            continue
          }

          if (mediaResult.reason === 'document_type_excluded') {
            this.logger.info('whatsapp_inbound_message_ignored', {
              reason: mediaResult.reason,
              mimetype: mediaResult.excludedMimetype ?? 'unknown',
              remoteJidKind: getJidKind(message.key.remoteJid ?? ''),
            })
            continue
          }

          const text = extractText(message)
          if (text === null) {
            this.logger.info('whatsapp_inbound_message_ignored', {
              reason: 'missing_text',
              remoteJidKind: getJidKind(message.key.remoteJid ?? ''),
            })
            continue
          }

          const parsedMessage: IncomingWhatsAppMessage = {
            contractVersion: '1',
            channelMessageId: envelopeResult.envelope.messageId,
            channelIdempotencyKey: envelopeResult.envelope.channelIdempotencyKey,
            correlationId: envelopeResult.envelope.correlationId,
            senderPhone: envelopeResult.envelope.senderPhone,
            conversationKind: 'individual',
            fromMe: envelopeResult.envelope.fromMe,
            receivedAtUtc: envelopeResult.envelope.receivedAtUtc,
            kind: 'text',
            text,
          }

          this.logger.info('whatsapp_inbound_message_received', {
            channelMessageId: parsedMessage.channelMessageId,
            correlationId: parsedMessage.correlationId,
          })
          for (const listener of this.incomingMessageListeners) {
            listener(parsedMessage)
          }
        }
      }

      removeConnectionListener = socket.onConnectionUpdate(onConnectionUpdate)
      removeCredentialsListener = socket.onCredentialsUpdate(onCredentialsUpdate)
      removeMessagesListener = socket.onMessagesUpsert(onMessagesUpsert)
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

type RawBaileysSocket = Pick<ReturnType<typeof makeWASocket>, 'onWhatsApp' | 'sendMessage' | 'end'> & {
  ev: Pick<ReturnType<typeof makeWASocket>['ev'], 'on' | 'off'>
  ws: {
    on(event: string, listener: (node: BinaryNode) => void): unknown
    off(event: string, listener: (node: BinaryNode) => void): unknown
  }
}

export function createBaileysSocket(
  auth: AuthenticationState,
  logger: SafeLogger,
  outboundMessages: OutboundMessageCache,
  socketFactory: (config: UserFacingSocketConfig) => RawBaileysSocket = makeWASocket,
): BaileysSessionSocket {
  const socket = socketFactory({
    auth,
    getMessage: outboundMessages.getMessage,
    logger: pino({
      level: 'debug',
      hooks: {
        logMethod(args, _method, level) {
          const message = typeof args[0] === 'string' ? args[0] : args[1]
          switch (message) {
            case 'recv retry request':
              logger.info('whatsapp_retry_requested')
              break
            case 'forced new session for retry recp':
              logger.info('whatsapp_retry_session_rebuilt')
              break
            case 'recv retry request, but message not available':
              logger.warn('whatsapp_retry_original_message_missing')
              break
            case 'error in sending message again':
              logger.error('whatsapp_retry_send_failed')
              break
            case 'will not send message again, as sent too many times':
              logger.error('whatsapp_retry_limit_reached')
              break
            default:
              // SDK fields can contain message content, JIDs and keys; never forward them.
              if (level >= 50) {
                logger.error('whatsapp_sdk_error')
              } else if (level >= 40) {
                logger.warn('whatsapp_sdk_warning')
              }
          }
        },
      },
    }),
  })

  const onMessageUpdates = (updates: WAMessageUpdate[]) => {
    for (const { key, update } of updates) {
      if (key.fromMe !== true || typeof key.id !== 'string' || !/^[A-Za-z0-9_-]{1,128}$/.test(key.id)) {
        continue
      }
      let deliveryStatus: string
      switch (update.status) {
        case proto.WebMessageInfo.Status.ERROR: deliveryStatus = 'error'; break
        case proto.WebMessageInfo.Status.SERVER_ACK: deliveryStatus = 'server_ack'; break
        case proto.WebMessageInfo.Status.DELIVERY_ACK: deliveryStatus = 'delivered'; break
        case proto.WebMessageInfo.Status.READ: deliveryStatus = 'read'; break
        case proto.WebMessageInfo.Status.PLAYED: deliveryStatus = 'played'; break
        default: continue
      }
      logger.info('whatsapp_message_delivery_update', { messageId: key.id, deliveryStatus })
    }
  }
  socket.ev.on('messages.update', onMessageUpdates)

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
    onMessagesUpsert(listener) {
      const handler = (event: { messages: WAMessage[]; type: string }) => {
        listener(event.messages, event.type)
      }
      socket.ev.on('messages.upsert', handler)
      return () => socket.ev.off('messages.upsert', handler)
    },
    onMessageAck(listener) {
      const handler = (node: BinaryNode) => {
        listener(node.attrs.id ?? '', node.attrs.error)
      }
      socket.ws.on('CB:ack,class:message', handler)
      return () => { socket.ws.off('CB:ack,class:message', handler) }
    },
    async resolveRecipient(recipientJid) {
      const recipients = await socket.onWhatsApp(recipientJid)
      if (recipients === undefined) {
        throw new Error('O WhatsApp não confirmou a consulta do destinatário.')
      }
      const recipient = recipients.find((entry) => entry.exists)
      if (recipient === undefined) {
        throw new PermanentChannelSendError(
          'RECIPIENT_NOT_ON_WHATSAPP',
          'O WhatsApp não encontrou uma conta para o telefone informado.',
        )
      }
      const aliases = [recipientJid]
      if (typeof recipient.lid === 'string' && recipient.lid.endsWith('@lid')) {
        aliases.push(recipient.lid)
      }
      return { jid: recipient.jid, aliases }
    },
    sendMessage(recipientJid, text, messageId, recipientAliases = []) {
      return outboundMessages.send(recipientJid, messageId, recipientAliases,
        () => socket.sendMessage(recipientJid, { text }, { messageId }))
    },
    end(error) {
      socket.ev.off('messages.update', onMessageUpdates)
      socket.end(error)
    },
  }
}

type EnvelopeFailureReason =
  | 'missing_remote_jid'
  | 'missing_message_id'
  | 'group_message'
  | 'from_me'
  | 'invalid_sender_phone'

interface ParsedEnvelope {
  messageId: string
  fromMe: boolean
  senderPhone: string
  channelIdempotencyKey: string
  correlationId: string
  receivedAtUtc: string
}

type EnvelopeParseResult =
  | { envelope: ParsedEnvelope; reason: null }
  | { envelope: null; reason: EnvelopeFailureReason }

/**
 * Extrai e valida os campos de envelope compartilhados por texto e mídia —
 * remetente individual, identificador estável e chave idempotente. Usado
 * como primeiro estágio antes de decidir se a mensagem é mídia ou texto.
 */
function parseEnvelope(message: WAMessage): EnvelopeParseResult {
  const key = message.key
  const remoteJid = key.remoteJid ?? ''
  const messageId = key.id
  const fromMe = key.fromMe ?? false
  if (remoteJid === '') {
    return { envelope: null, reason: 'missing_remote_jid' }
  }
  if (messageId === undefined || messageId === null || messageId === '') {
    return { envelope: null, reason: 'missing_message_id' }
  }
  if (remoteJid.endsWith('@g.us')) {
    return { envelope: null, reason: 'group_message' }
  }
  if (fromMe) {
    return { envelope: null, reason: 'from_me' }
  }

  const senderJid = key.senderPn ?? key.participantPn ?? remoteJid
  const senderPhone = /^(?<phone>[1-9]\d{7,14})(?::\d+)?@s\.whatsapp\.net$/.exec(senderJid)?.groups?.phone
  if (senderPhone === undefined) {
    return { envelope: null, reason: 'invalid_sender_phone' }
  }

  const channelIdempotencyKey = createHash('sha256')
    .update(`${remoteJid}\u0000${messageId}\u0000${fromMe ? '1' : '0'}`)
    .digest('hex')
  const timestampSeconds = Number(message.messageTimestamp)
  const receivedAtUtc = Number.isFinite(timestampSeconds) && timestampSeconds > 0
    ? new Date(timestampSeconds * 1_000)
    : new Date()

  return {
    reason: null,
    envelope: {
      messageId,
      fromMe,
      senderPhone,
      channelIdempotencyKey,
      correlationId: randomUUID(),
      receivedAtUtc: receivedAtUtc.toISOString(),
    },
  }
}

/** Extrai o texto de uma mensagem já validada como envelope — nunca usa legenda de mídia (mídia é um evento único). */
function extractText(message: WAMessage): string | null {
  const body = normalizeMessageContent(message.message)
  const text = body?.conversation ?? body?.extendedTextMessage?.text ?? null
  return typeof text === 'string' && text.trim() !== '' ? text : null
}

interface IncomingMediaMetadata {
  mediaKind: IncomingWhatsAppMediaKind
  contentType: string
  isVoiceNote: boolean
  durationSeconds: number | null
  caption: string | null
}

type MediaMetadataFailureReason = 'no_media_content' | 'document_type_excluded' | 'missing_mimetype_or_length'

type MediaMetadataParseResult =
  | { media: IncomingMediaMetadata; reason: null }
  | { media: null; reason: MediaMetadataFailureReason; excludedMimetype?: string | null }

/**
 * Identifica se a mensagem é uma mídia aceita (áudio, imagem, vídeo ou
 * PDF) e extrai os metadados declarados pelo WhatsApp — nunca baixa o
 * binário aqui (feito separadamente por {@link BaileysSession.deliverIncomingMedia},
 * apenas quando a mensagem é aceita). Documentos com mimetype de imagem
 * (JPEG/PNG/WebP) são relatados como imagem. Figurinhas e demais documentos
 * que não sejam PDF são excluídos (decisão do PO, 2026-10-21) — nunca chegam
 * a ser relatados como mídia nem como texto.
 */
function extractMediaMetadata(message: WAMessage): MediaMetadataParseResult {
  const body = normalizeMessageContent(message.message)
  if (body?.imageMessage) {
    return buildMediaMetadata('image', body.imageMessage.mimetype, body.imageMessage.fileLength,
      false, null, body.imageMessage.caption)
  }
  if (body?.videoMessage) {
    return buildMediaMetadata('video', body.videoMessage.mimetype, body.videoMessage.fileLength,
      false, body.videoMessage.seconds ?? null, body.videoMessage.caption)
  }
  if (body?.audioMessage) {
    return buildMediaMetadata('audio', body.audioMessage.mimetype, body.audioMessage.fileLength,
      body.audioMessage.ptt ?? false, body.audioMessage.seconds ?? null, null)
  }
  if (body?.documentMessage) {
    const documentMimetype = normalizeMimetype(body.documentMessage.mimetype)
    // Foto enviada "como documento" (comum para nota fiscal, preserva a
    // qualidade) é tratada como imagem.
    if (DOCUMENT_IMAGE_MIMETYPES.has(documentMimetype)) {
      return buildMediaMetadata('image', documentMimetype, body.documentMessage.fileLength,
        false, null, body.documentMessage.caption)
    }
    if (documentMimetype !== 'application/pdf') {
      return { media: null, reason: 'document_type_excluded', excludedMimetype: documentMimetype || null }
    }
    return buildMediaMetadata('document', documentMimetype, body.documentMessage.fileLength,
      false, null, body.documentMessage.caption)
  }

  return { media: null, reason: 'no_media_content' }
}

const DOCUMENT_IMAGE_MIMETYPES = new Set(['image/jpeg', 'image/png', 'image/webp'])

function normalizeMimetype(mimetype: string | null | undefined): string {
  return typeof mimetype === 'string' ? (mimetype.split(';')[0] ?? '').trim().toLowerCase() : ''
}

function buildMediaMetadata(
  mediaKind: IncomingWhatsAppMediaKind,
  mimetype: string | null | undefined,
  fileLength: unknown,
  isVoiceNote: boolean,
  durationSeconds: number | null,
  caption: string | null | undefined,
): MediaMetadataParseResult {
  if (typeof mimetype !== 'string' || mimetype === '' || fileLength === null || fileLength === undefined) {
    return { media: null, reason: 'missing_mimetype_or_length' }
  }

  return {
    reason: null,
    media: {
      mediaKind,
      contentType: mimetype,
      isVoiceNote,
      durationSeconds,
      caption: typeof caption === 'string' && caption.trim() !== '' ? caption : null,
    },
  }
}

/**
 * Baixa como stream (nunca `'buffer'`) e impõe {@link MAX_INBOUND_MEDIA_BYTES}
 * durante o consumo (R1, resolvido em 2026-10-22) — o binário nunca é
 * materializado por inteiro quando excede o limite.
 */
async function defaultDownloadMedia(message: WAMessage): Promise<Buffer> {
  const stream = await downloadMediaMessage(message, 'stream', {})
  return collectBufferWithLimit(stream, MAX_INBOUND_MEDIA_BYTES)
}

function getJidKind(jid: string): 'phone' | 'group' | 'lid' | 'other' {
  if (jid.endsWith('@s.whatsapp.net')) {
    return 'phone'
  }
  if (jid.endsWith('@g.us')) {
    return 'group'
  }
  if (jid.endsWith('@lid')) {
    return 'lid'
  }
  return 'other'
}

/**
 * Extrai o telefone de um JID pareado (`creds.me.id`, ex.:
 * `5511999999999:12@s.whatsapp.net`) — usado apenas para reportar o
 * `phoneNumber` do evento `connected` (ADR-049). Nunca usado para texto
 * inbound (fluxo separado em {@link parseEnvelope}).
 */
function extractPhoneFromJid(id: string | null | undefined): string | null {
  if (typeof id !== 'string' || id === '') {
    return null
  }

  const phone = /^(?<phone>[1-9]\d{7,14})(?::\d+)?@s\.whatsapp\.net$/.exec(id)?.groups?.phone
  return phone ?? null
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
