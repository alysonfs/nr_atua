import assert from 'node:assert/strict'
import { mkdtemp, rm, stat } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import {
  DisconnectReason,
  useMultiFileAuthState,
  type ConnectionState,
} from '@whiskeysockets/baileys'
import {
  BaileysSession,
  SessionFatalError,
  type BaileysSessionDependencies,
  type BaileysSessionSocket,
} from './baileys-session.js'
import type { SafeLogger } from './contracts.js'

const logger: SafeLogger = {
  info: () => {},
  warn: () => {},
  error: () => {},
}

class FakeSocket implements BaileysSessionSocket {
  private readonly connectionListeners = new Set<(update: Partial<ConnectionState>) => void>()
  private readonly credentialsListeners = new Set<() => void>()
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

  setSendMessageHandler(handler: BaileysSessionSocket['sendMessage']): void {
    this.sendMessageHandler = handler
  }

  get listenerCount(): number {
    return this.connectionListeners.size + this.credentialsListeners.size
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
  dependencies: Pick<BaileysSessionDependencies, 'loadAuthState' | 'wait'> = {},
): Promise<SessionHarness> {
  const directory = await mkdtemp(join(tmpdir(), 'atua-baileys-session-'))
  const sockets: FakeSocket[] = []
  const session = new BaileysSession(directory, true, logger, {
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

test('session directory permissions are restricted to the process user', async () => {
  const harness = await createHarness()

  try {
    await harness.waitForSocket(1)
    assert.equal((await stat(harness.directory)).mode & 0o777, 0o700)
  } finally {
    await harness.dispose()
  }
})
