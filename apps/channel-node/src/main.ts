import { ChannelApiClient } from './channel-api-client.js'
import { BaileysSession } from './baileys-session.js'
import { loadConfig } from './config.js'
import { consoleLogger } from './logger.js'
import { ChannelWorker } from './channel-worker.js'
import { IncomingMessageWorker } from './incoming-message-worker.js'
import { IncomingMediaWorker } from './incoming-media-worker.js'
import { SessionStateReporter } from './session-state-reporter.js'

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
    config.mediaIngestServiceToken,
    fetch,
    config.sessionReportServiceToken,
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
  const incomingMediaWorker = new IncomingMediaWorker(api, session, consoleLogger, config.mediaQueueCapacity)
  const sessionStateReporter = new SessionStateReporter(
    api,
    session,
    consoleLogger,
    config.sessionReportServiceToken !== undefined,
  )

  const sessionTask = session.run(controller.signal)
  const workerTask = worker.run(controller.signal)
  const incomingMessageTask = incomingMessageWorker.run(controller.signal)
  const incomingMediaTask = incomingMediaWorker.run(controller.signal)
  const sessionStateReportTask = sessionStateReporter.run(controller.signal)

  try {
    await Promise.race([sessionTask, workerTask, incomingMessageTask, incomingMediaTask, sessionStateReportTask])
  } finally {
    controller.abort()
    await Promise.allSettled([
      sessionTask,
      workerTask,
      incomingMessageTask,
      incomingMediaTask,
      sessionStateReportTask,
    ])
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
