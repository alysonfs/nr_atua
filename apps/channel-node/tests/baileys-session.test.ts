import assert from 'node:assert/strict'
import { mkdtemp, rm, stat } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import {
  DisconnectReason,
  useMultiFileAuthState,
  type ConnectionState,
  type WAMessage,
} from '@whiskeysockets/baileys'
import {
  BaileysSession,
  SessionFatalError,
  type BaileysSessionDependencies,
  type BaileysSessionSocket,
} from '../src/baileys-session.js'
import type { SafeLogger } from '../src/contracts.js'

const logger: SafeLogger = {
  info: () => {},
  warn: () => {},
  error: () => {},
}

class FakeSocket implements BaileysSessionSocket {
  private readonly connectionListeners = new Set<(update: Partial<ConnectionState>) => void>()
  private readonly credentialsListeners = new Set<() => void>()
  private readonly messageListeners = new Set<(messages: WAMessage[], type: string) => void>()
  private readonly ackListeners = new Set<(messageId: string, errorCode?: string) => void>()
  resolvedJid: string | undefined
  private sendMessageHandler: BaileysSessionSocket['sendMessage'] = async (_jid, _text, messageId) => ({
    key: { id: messageId },
  })
  readonly endCalls: Array<Error | undefined> = []

  onConnectionUpdate(listener: (update: Partial<ConnectionState>) => void): () => void {
    this.connectionListeners.add(listener)
    return () => this.connectionListeners.delete(listener)
  }

  onCredentialsUpdate(listener: () => void): () => void {
    this.credentialsListeners.add(listener)
    return () => this.credentialsListeners.delete(listener)
  }

  onMessagesUpsert(listener: (messages: WAMessage[], type: string) => void): () => void {
    this.messageListeners.add(listener)
    return () => this.messageListeners.delete(listener)
  }

  onMessageAck(listener: (messageId: string, errorCode?: string) => void): () => void {
    this.ackListeners.add(listener)
    return () => this.ackListeners.delete(listener)
  }

  async resolveRecipient(recipientJid: string): Promise<string> {
    return this.resolvedJid ?? recipientJid
  }

  emitAck(messageId: string, errorCode?: string): void {
    for (const listener of this.ackListeners) {
      listener(messageId, errorCode)
    }
  }

  sendMessage(
    recipientJid: string,
    text: string,
    messageId: string,
  ): ReturnType<BaileysSessionSocket['sendMessage']> {
    return this.sendMessageHandler(recipientJid, text, messageId)
  }

  end(error?: Error): void {
    this.endCalls.push(error)
  }

  emitConnectionUpdate(update: Partial<ConnectionState>): void {
    for (const listener of this.connectionListeners) {
      listener(update)
    }
  }

  emitCredentialsUpdate(): void {
    for (const listener of this.credentialsListeners) {
      listener()
    }
  }

  emitMessagesUpsert(messages: WAMessage[], type = 'notify'): void {
    for (const listener of this.messageListeners) {
      listener(messages, type)
    }
  }

  setSendMessageHandler(handler: BaileysSessionSocket['sendMessage']): void {
    this.sendMessageHandler = handler
  }

  get listenerCount(): number {
    return this.connectionListeners.size + this.credentialsListeners.size + this.ackListeners.size
  }
}

interface SessionHarness {
  directory: string
  session: BaileysSession
  controller: AbortController
  task: Promise<void>
  sockets: FakeSocket[]
  waitForSocket(index: number): Promise<FakeSocket>
  dispose(): Promise<void>
}

async function createHarness(
  dependencies: Pick<BaileysSessionDependencies, 'loadAuthState' | 'wait' | 'downloadMedia'> = {},
  sessionLogger: SafeLogger = logger,
): Promise<SessionHarness> {
  const directory = await mkdtemp(join(tmpdir(), 'atua-baileys-session-'))
  const sockets: FakeSocket[] = []
  const session = new BaileysSession(directory, true, sessionLogger, {
    ...dependencies,
    createSocket: () => {
      const socket = new FakeSocket()
      sockets.push(socket)
      return socket
    },
  })
  const controller = new AbortController()
  const task = session.run(controller.signal)
  void task.catch(() => {})

  return {
    directory,
    session,
    controller,
    task,
    sockets,
    async waitForSocket(index) {
      for (let attempt = 0; attempt < 1000 && sockets.length < index; attempt += 1) {
        await new Promise<void>((resolve) => setImmediate(resolve))
      }

      const socket = sockets[index - 1]
      assert.ok(socket, `Expected socket ${index} to be created.`)
      return socket
    },
    async dispose() {
      controller.abort()
      await task.catch(() => {})
      await rm(directory, { recursive: true, force: true })
    },
  }
}

function deferred(): { promise: Promise<void>; resolve(): void } {
  let resolve!: () => void
  const promise = new Promise<void>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

test('reconnects after a transient disconnect and resets backoff after opening', async () => {
  const retryDelays: number[] = []
  const harness = await createHarness({
    wait: async (milliseconds) => {
      retryDelays.push(milliseconds)
    },
  })

  try {
    const firstSocket = await harness.waitForSocket(1)
    firstSocket.emitConnectionUpdate({ connection: 'open' })
    await harness.session.waitUntilReady(harness.controller.signal)
    firstSocket.emitConnectionUpdate({
      connection: 'close',
      lastDisconnect: { error: new Error('temporary disconnect'), date: new Date() },
    })

    const secondSocket = await harness.waitForSocket(2)
    assert.deepEqual(retryDelays, [1_000])
    secondSocket.emitConnectionUpdate({ connection: 'open' })
    await harness.session.waitUntilReady(harness.controller.signal)
    secondSocket.emitConnectionUpdate({
      connection: 'close',
      lastDisconnect: { error: new Error('temporary disconnect'), date: new Date() },
    })

    await harness.waitForSocket(3)
    assert.deepEqual(retryDelays, [1_000, 1_000])
  } finally {
    await harness.dispose()
  }
})

test('forwards only individual inbound text messages with a stable idempotency key', async () => {
  const harness = await createHarness()
  const received: Array<{
    channelMessageId: string
    senderPhone: string
    text: string | null
    channelIdempotencyKey: string
  }> = []
  const unsubscribe = harness.session.onIncomingMessage((message) => received.push(message))

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    await harness.session.waitUntilReady(harness.controller.signal)

    const validMessage = {
      key: {
        remoteJid: '5511999991234@s.whatsapp.net',
        senderPn: '5511999991234@s.whatsapp.net',
        id: 'wamid-1',
        fromMe: false,
      },
      message: { conversation: 'O produto não liga.' },
      messageTimestamp: 1_792_243_200,
    } as WAMessage
    const groupMessage = {
      ...validMessage,
      key: { ...validMessage.key, remoteJid: '120363000000000000@g.us' },
    } as WAMessage
    const ownMessage = {
      ...validMessage,
      key: { ...validMessage.key, fromMe: true },
    } as WAMessage
    const emptyMessage = {
      ...validMessage,
      key: { ...validMessage.key, id: 'wamid-empty' },
      message: {},
    } as WAMessage

    socket.emitMessagesUpsert([validMessage, groupMessage, ownMessage, emptyMessage])
    socket.emitMessagesUpsert([validMessage])
    socket.emitMessagesUpsert([validMessage], 'append')

    assert.equal(received.length, 2)
    assert.equal(received[0]?.channelMessageId, 'wamid-1')
    assert.equal(received[0]?.senderPhone, '5511999991234')
    assert.equal(received[0]?.text, 'O produto não liga.')
    assert.equal(received[0]?.channelIdempotencyKey.length, 64)
    assert.equal(received[0]?.channelIdempotencyKey, received[1]?.channelIdempotencyKey)
  } finally {
    unsubscribe()
    await harness.dispose()
  }
})

test('extracts and delivers audio, image, video and PDF media as a single event, excluding non-PDF documents and never falling back to text', async () => {
  const downloadedMessages: WAMessage[] = []
  const harness = await createHarness({
    downloadMedia: async (message) => {
      downloadedMessages.push(message)
      return Buffer.from('synthetic-media-bytes')
    },
  })
  const receivedMedia: Array<{ channelMessageId: string; mediaKind: string; contentType: string; isVoiceNote: boolean }> = []
  const receivedText: Array<{ channelMessageId: string }> = []
  const unsubscribeMedia = harness.session.onIncomingMedia((media) => receivedMedia.push(media))
  const unsubscribeText = harness.session.onIncomingMessage((message) => receivedText.push(message))

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    await harness.session.waitUntilReady(harness.controller.signal)

    const baseKey = {
      remoteJid: '5511999991234@s.whatsapp.net',
      senderPn: '5511999991234@s.whatsapp.net',
      fromMe: false,
    }
    const audioMessage = {
      key: { ...baseKey, id: 'wamid-audio' },
      message: { audioMessage: { mimetype: 'audio/ogg; codecs=opus', fileLength: 1_024, seconds: 7, ptt: true } },
      messageTimestamp: 1_792_243_200,
    } as unknown as WAMessage
    const imageMessage = {
      key: { ...baseKey, id: 'wamid-image' },
      message: { imageMessage: { mimetype: 'image/jpeg', fileLength: 2_048, caption: 'legenda da imagem' } },
      messageTimestamp: 1_792_243_200,
    } as unknown as WAMessage
    const videoMessage = {
      key: { ...baseKey, id: 'wamid-video' },
      message: { videoMessage: { mimetype: 'video/mp4', fileLength: 4_096, seconds: 12, caption: 'legenda do vídeo' } },
      messageTimestamp: 1_792_243_200,
    } as unknown as WAMessage
    const pdfMessage = {
      key: { ...baseKey, id: 'wamid-pdf' },
      message: { documentMessage: { mimetype: 'application/pdf', fileLength: 8_192, fileName: 'laudo.pdf' } },
      messageTimestamp: 1_792_243_200,
    } as unknown as WAMessage
    const excludedDocumentMessage = {
      key: { ...baseKey, id: 'wamid-docx' },
      message: { documentMessage: { mimetype: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', fileLength: 512 } },
      messageTimestamp: 1_792_243_200,
    } as unknown as WAMessage

    socket.emitMessagesUpsert([audioMessage, imageMessage, videoMessage, pdfMessage, excludedDocumentMessage])

    for (let attempt = 0; attempt < 1_000 && receivedMedia.length < 4; attempt += 1) {
      await new Promise<void>((resolve) => setImmediate(resolve))
    }

    assert.equal(receivedMedia.length, 4)
    assert.equal(downloadedMessages.length, 4)
    assert.equal(receivedText.length, 0)

    const audio = receivedMedia.find((item) => item.channelMessageId === 'wamid-audio')
    assert.equal(audio?.mediaKind, 'audio')
    assert.equal(audio?.contentType, 'audio/ogg; codecs=opus')
    assert.equal(audio?.isVoiceNote, true)

    const image = receivedMedia.find((item) => item.channelMessageId === 'wamid-image')
    assert.equal(image?.mediaKind, 'image')
    assert.equal(image?.contentType, 'image/jpeg')

    const video = receivedMedia.find((item) => item.channelMessageId === 'wamid-video')
    assert.equal(video?.mediaKind, 'video')

    const pdf = receivedMedia.find((item) => item.channelMessageId === 'wamid-pdf')
    assert.equal(pdf?.mediaKind, 'document')
    assert.equal(pdf?.contentType, 'application/pdf')

    assert.equal(receivedMedia.some((item) => item.channelMessageId === 'wamid-docx'), false)
  } finally {
    unsubscribeMedia()
    unsubscribeText()
    await harness.dispose()
  }
})

test('a media download failure is logged and discarded without a text fallback', async () => {
  const harness = await createHarness({
    downloadMedia: async () => {
      throw new Error('transient network failure')
    },
  })
  const receivedMedia: unknown[] = []
  const receivedText: unknown[] = []
  const unsubscribeMedia = harness.session.onIncomingMedia((media) => receivedMedia.push(media))
  const unsubscribeText = harness.session.onIncomingMessage((message) => receivedText.push(message))

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    await harness.session.waitUntilReady(harness.controller.signal)

    const audioMessage = {
      key: {
        remoteJid: '5511999991234@s.whatsapp.net',
        senderPn: '5511999991234@s.whatsapp.net',
        id: 'wamid-audio-fail',
        fromMe: false,
      },
      message: { audioMessage: { mimetype: 'audio/ogg', fileLength: 1_024, ptt: true } },
      messageTimestamp: 1_792_243_200,
    } as unknown as WAMessage

    socket.emitMessagesUpsert([audioMessage])
    await new Promise<void>((resolve) => setTimeout(resolve, 20))

    assert.equal(receivedMedia.length, 0)
    assert.equal(receivedText.length, 0)
  } finally {
    unsubscribeMedia()
    unsubscribeText()
    await harness.dispose()
  }
})

test('an image or video caption is never forwarded as a standalone text message', async () => {
  const harness = await createHarness({
    downloadMedia: async () => Buffer.from('synthetic-bytes'),
  })
  const receivedText: unknown[] = []
  const receivedMedia: Array<{ caption: string | null }> = []
  const unsubscribeText = harness.session.onIncomingMessage((message) => receivedText.push(message))
  const unsubscribeMedia = harness.session.onIncomingMedia((media) => receivedMedia.push(media))

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    await harness.session.waitUntilReady(harness.controller.signal)

    const imageWithCaption = {
      key: {
        remoteJid: '5511999991234@s.whatsapp.net',
        senderPn: '5511999991234@s.whatsapp.net',
        id: 'wamid-image-caption',
        fromMe: false,
      },
      message: { imageMessage: { mimetype: 'image/png', fileLength: 1_000, caption: 'legenda' } },
      messageTimestamp: 1_792_243_200,
    } as unknown as WAMessage

    socket.emitMessagesUpsert([imageWithCaption])

    for (let attempt = 0; attempt < 1_000 && receivedMedia.length < 1; attempt += 1) {
      await new Promise<void>((resolve) => setImmediate(resolve))
    }

    assert.equal(receivedText.length, 0)
    assert.equal(receivedMedia.length, 1)
    assert.equal(receivedMedia[0]?.caption, 'legenda')
  } finally {
    unsubscribeText()
    unsubscribeMedia()
    await harness.dispose()
  }
})

test('logs sanitized inbound upsert and filtering diagnostics', async () => {
  const events: Array<{
    event: string
    fields?: Record<string, string | number | boolean>
  }> = []
  const captureLogger: SafeLogger = {
    info: (event, fields) => events.push({ event, fields }),
    warn: (event, fields) => events.push({ event, fields }),
    error: (event, fields) => events.push({ event, fields }),
  }
  const harness = await createHarness({}, captureLogger)

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    await harness.session.waitUntilReady(harness.controller.signal)

    const message = {
      key: {
        remoteJid: '5511999991234@s.whatsapp.net',
        senderPn: '5511999991234@s.whatsapp.net',
        id: 'wamid-diagnostic',
        fromMe: false,
      },
      message: { conversation: 'private test content' },
      messageTimestamp: 1_792_243_200,
    } as WAMessage
    const ownMessage = {
      ...message,
      key: { ...message.key, fromMe: true },
    } as WAMessage
    const groupMessage = {
      ...message,
      key: { ...message.key, remoteJid: '120363000000000000@g.us' },
    } as WAMessage
    const lidMessage = {
      key: {
        remoteJid: '1234567890@lid',
        id: 'wamid-lid',
        fromMe: false,
      },
      message: { conversation: 'private test content' },
    } as WAMessage

    socket.emitMessagesUpsert([message, ownMessage, groupMessage, lidMessage])
    socket.emitMessagesUpsert([message], 'append')

    assert.ok(events.some(({ event, fields }) =>
      event === 'whatsapp_inbound_upsert_received' && fields?.messageCount === 4))
    assert.ok(events.some(({ event, fields }) =>
      event === 'whatsapp_inbound_message_ignored' && fields?.reason === 'from_me'))
    assert.ok(events.some(({ event, fields }) =>
      event === 'whatsapp_inbound_message_ignored' && fields?.reason === 'group_message'))
    assert.ok(events.some(({ event, fields }) =>
      event === 'whatsapp_inbound_message_ignored' &&
      fields?.reason === 'invalid_sender_phone' &&
      fields?.remoteJidKind === 'lid'))
    assert.ok(events.some(({ event }) => event === 'whatsapp_inbound_message_received'))
    assert.ok(events.some(({ event, fields }) =>
      event === 'whatsapp_inbound_upsert_ignored' &&
      fields?.upsertType === 'append' &&
      fields?.reason === 'non_notify'))

    const serializedEvents = JSON.stringify(events)
    assert.equal(serializedEvents.includes('5511999991234'), false)
    assert.equal(serializedEvents.includes('private test content'), false)
    assert.equal(serializedEvents.includes('@s.whatsapp.net'), false)
  } finally {
    await harness.dispose()
  }
})

test('a logged-out session fails without reconnecting', async () => {
  const harness = await createHarness()

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({
      connection: 'close',
      lastDisconnect: {
        error: Object.assign(new Error('logged out'), {
          output: { statusCode: DisconnectReason.loggedOut },
        }),
        date: new Date(),
      },
    })

    await assert.rejects(harness.task, SessionFatalError)
    assert.equal(harness.sockets.length, 1)
  } finally {
    await harness.dispose()
  }
})

test('send timeout rejects and shutdown closes the socket', async () => {
  const harness = await createHarness()

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    await harness.session.waitUntilReady(harness.controller.signal)
    socket.setSendMessageHandler(() => new Promise(() => {}))

    await assert.rejects(
      harness.session.sendText(
        '5511999991234@s.whatsapp.net',
        'Test message',
        'stable-message-id',
        5,
        harness.controller.signal,
      ),
      /Envio WhatsApp expirou/,
    )

    harness.controller.abort()
    await harness.task
    assert.equal(socket.endCalls.length, 1)
    assert.equal(socket.listenerCount, 0)
  } finally {
    await harness.dispose()
  }
})

test('a locally generated message ID is not a server acknowledgement', async () => {
  const harness = await createHarness()

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    await assert.rejects(
      harness.session.sendText('5584994241248@s.whatsapp.net', 'Test', 'message-id', 10, harness.controller.signal),
      /Envio WhatsApp expirou/,
    )
    assert.equal(socket.listenerCount, 2)
  } finally {
    await harness.dispose()
  }
})

test('uses the WhatsApp-resolved recipient and waits for the matching server ack', async () => {
  const harness = await createHarness()

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    socket.resolvedJid = '558494241248@s.whatsapp.net'
    socket.setSendMessageHandler(async (jid, _text, messageId) => {
      assert.equal(jid, socket.resolvedJid)
      socket.emitAck('unrelated-message', '500')
      socket.emitAck(messageId)
      return { key: { id: messageId } }
    })

    await harness.session.sendText(
      '5584994241248@s.whatsapp.net', 'Test', 'message-id', 100, harness.controller.signal,
    )
    assert.equal(socket.listenerCount, 2)
  } finally {
    await harness.dispose()
  }
})

test('server rejection cannot complete a send successfully', async () => {
  const harness = await createHarness()

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    socket.setSendMessageHandler(async (_jid, _text, messageId) => {
      socket.emitAck(messageId, '463')
      return { key: { id: messageId } }
    })
    await assert.rejects(
      harness.session.sendText('5584994241248@s.whatsapp.net', 'Test', 'message-id', 100, harness.controller.signal),
      /servidor WhatsApp rejeitou/,
    )
    assert.equal(socket.listenerCount, 2)
  } finally {
    await harness.dispose()
  }
})

test('disconnect before server ack fails the send and removes listeners', async () => {
  const harness = await createHarness()

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    socket.setSendMessageHandler(async (_jid, _text, messageId) => {
      socket.emitConnectionUpdate({ connection: 'close' })
      return { key: { id: messageId } }
    })
    await assert.rejects(
      harness.session.sendText('5584994241248@s.whatsapp.net', 'Test', 'message-id', 100, harness.controller.signal),
      /desconectou antes da confirmação/,
    )
    assert.equal(socket.listenerCount, 0)
  } finally {
    await harness.dispose()
  }
})

test('recipient lookup finishing after timeout cannot send a late message', async () => {
  const harness = await createHarness()
  const lookup = deferred()

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    socket.resolveRecipient = async (jid) => { await lookup.promise; return jid }
    let sends = 0
    socket.setSendMessageHandler(async (_jid, _text, messageId) => {
      sends += 1
      return { key: { id: messageId } }
    })
    await assert.rejects(
      harness.session.sendText('5584994241248@s.whatsapp.net', 'Test', 'message-id', 10, harness.controller.signal),
      /Envio WhatsApp expirou/,
    )
    lookup.resolve()
    await new Promise<void>((resolve) => setImmediate(resolve))
    assert.equal(sends, 0)
    assert.equal(socket.listenerCount, 2)
  } finally {
    lookup.resolve()
    await harness.dispose()
  }
})

test('graceful shutdown waits for pending credential persistence', async () => {
  const saveStarted = deferred()
  const releaseSave = deferred()
  const loadAuthState: NonNullable<BaileysSessionDependencies['loadAuthState']> = async (path) => {
    const auth = await useMultiFileAuthState(path)
    return {
      ...auth,
      saveCreds: async () => {
        saveStarted.resolve()
        await releaseSave.promise
      },
    }
  }
  const harness = await createHarness({ loadAuthState })

  try {
    const socket = await harness.waitForSocket(1)
    socket.emitConnectionUpdate({ connection: 'open' })
    await harness.session.waitUntilReady(harness.controller.signal)
    socket.emitCredentialsUpdate()
    await saveStarted.promise

    let settled = false
    void harness.task.then(
      () => { settled = true },
      () => { settled = true },
    )
    harness.controller.abort()
    await new Promise<void>((resolve) => setImmediate(resolve))

    assert.equal(settled, false)
    releaseSave.resolve()
    await harness.task
    assert.equal(socket.listenerCount, 0)
  } finally {
    releaseSave.resolve()
    await harness.dispose()
  }
})

async function runWithoutQr(
  me: { id: string } | undefined,
): Promise<{ sockets: FakeSocket[]; outcome: Promise<unknown>; dispose(): Promise<void> }> {
  const directory = await mkdtemp(join(tmpdir(), 'atua-baileys-session-'))
  const sockets: FakeSocket[] = []
  const session = new BaileysSession(directory, false, logger, {
    loadAuthState: async (path) => {
      const auth = await useMultiFileAuthState(path)
      auth.state.creds.registered = false
      auth.state.creds.me = me
      return auth
    },
    createSocket: () => {
      const socket = new FakeSocket()
      sockets.push(socket)
      return socket
    },
  })
  const controller = new AbortController()
  const outcome = session.run(controller.signal).then(
    () => 'stopped',
    (error: unknown) => error,
  )
  return {
    sockets,
    outcome,
    async dispose() {
      controller.abort()
      await outcome
      await rm(directory, { recursive: true, force: true })
    },
  }
}

test('a QR-paired session connects without printing a QR code', async () => {
  const run = await runWithoutQr({ id: '558400000000:1@s.whatsapp.net' })

  try {
    for (let attempt = 0; attempt < 1000 && run.sockets.length < 1; attempt += 1) {
      await new Promise<void>((resolve) => setImmediate(resolve))
    }
    assert.equal(run.sockets.length, 1)
  } finally {
    await run.dispose()
  }
})

test('an unpaired session without QR fails with a fatal session error', async () => {
  const run = await runWithoutQr(undefined)

  try {
    assert.ok((await run.outcome) instanceof SessionFatalError)
    assert.equal(run.sockets.length, 0)
  } finally {
    await run.dispose()
  }
})

test('session directory permissions are restricted to the process user', async () => {
  const harness = await createHarness()

  try {
    await harness.waitForSocket(1)
    assert.equal((await stat(harness.directory)).mode & 0o777, 0o700)
  } finally {
    await harness.dispose()
  }
})
