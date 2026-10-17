import { ChannelApiClient } from './channel-api-client.js'
import { BaileysSession } from './baileys-session.js'
import { loadConfig } from './config.js'
import { consoleLogger } from './logger.js'
import { ChannelWorker } from './channel-worker.js'
import { IncomingMessageWorker } from './incoming-message-worker.js'

async function main(): Promise<void> {
  const config = loadConfig()
  process.umask(0o077)

  const controller = new AbortController()
  const onShutdown = () => {
    consoleLogger.info('channel_shutdown_requested')
    controller.abort()
  }
  process.once('SIGINT', onShutdown)
  process.once('SIGTERM', onShutdown)

  const api = new ChannelApiClient(
    config.apiBaseUrl,
    config.claimServiceToken,
    config.completeServiceToken,
    config.messageIngestServiceToken,
  )
  const session = new BaileysSession(config.sessionPath, config.printQr, consoleLogger)
  const worker = new ChannelWorker(
    api,
    session,
    {
      pollingIntervalMs: config.pollingIntervalMs,
      sendTimeoutMs: config.sendTimeoutMs,
    },
    consoleLogger,
  )
  const incomingMessageWorker = new IncomingMessageWorker(api, session, consoleLogger)

  const sessionTask = session.run(controller.signal)
  const workerTask = worker.run(controller.signal)
  const incomingMessageTask = incomingMessageWorker.run(controller.signal)

  try {
    await Promise.race([sessionTask, workerTask, incomingMessageTask])
  } finally {
    controller.abort()
    await Promise.allSettled([sessionTask, workerTask, incomingMessageTask])
    process.off('SIGINT', onShutdown)
    process.off('SIGTERM', onShutdown)
  }
}

main().catch((error: unknown) => {
  console.error(JSON.stringify({
    timestamp: new Date().toISOString(),
    level: 'error',
    event: 'channel_process_stopped',
    errorType: error instanceof Error ? error.name : 'UnknownError',
  }))
  process.exitCode = 1
})
