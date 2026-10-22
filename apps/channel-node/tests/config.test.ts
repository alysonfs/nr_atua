import assert from 'node:assert/strict'
import test from 'node:test'
import { loadConfig } from '../src/config.js'

const validEnvironment: NodeJS.ProcessEnv = {
  ChannelWorker__ApiBaseUrl: 'http://127.0.0.1:5080',
  ChannelWorker__ClaimServiceToken: 'claim-test-token',
  ChannelWorker__CompleteServiceToken: 'complete-test-token',
  ChannelWorker__MessageIngestServiceToken: 'message-ingest-test-token',
  ChannelWorker__MediaIngestServiceToken: 'media-ingest-test-token',
  ChannelWorker__SessionPath: '/tmp/atua-channel-session',
}

test('loadConfig accepts isolated credentials and an external session path', () => {
  const config = loadConfig(validEnvironment)

  assert.equal(config.apiBaseUrl, 'http://127.0.0.1:5080')
  assert.equal(config.pollingIntervalMs, 15_000)
  assert.equal(config.sendTimeoutMs, 30_000)
  assert.equal(config.printQr, false)
  assert.equal(config.sessionPath, '/tmp/atua-channel-session')
  assert.equal(config.sessionReportServiceToken, undefined)
  assert.equal(config.mediaQueueCapacity, 20)
})

test('loadConfig accepts an optional, distinct session-report credential', () => {
  const config = loadConfig({
    ...validEnvironment,
    ChannelWorker__SessionReportServiceToken: 'session-report-test-token',
  })

  assert.equal(config.sessionReportServiceToken, 'session-report-test-token')
})

test('loadConfig rejects a session-report credential shared with another scope', () => {
  assert.throws(
    () => loadConfig({
      ...validEnvironment,
      ChannelWorker__SessionReportServiceToken: validEnvironment.ChannelWorker__ClaimServiceToken,
    }),
    /devem ser distintas/,
  )
})

test('loadConfig accepts a custom media queue capacity and rejects out-of-range values', () => {
  const config = loadConfig({
    ...validEnvironment,
    ChannelWorker__MediaQueueCapacity: '5',
  })
  assert.equal(config.mediaQueueCapacity, 5)

  assert.throws(
    () => loadConfig({
      ...validEnvironment,
      ChannelWorker__MediaQueueCapacity: '0',
    }),
    /entre 1 e 1000/,
  )
})

test('loadConfig rejects shared credentials', () => {
  assert.throws(
    () => loadConfig({
      ...validEnvironment,
      ChannelWorker__CompleteServiceToken: validEnvironment.ChannelWorker__ClaimServiceToken,
    }),
    /devem ser distintas/,
  )
})

test('loadConfig rejects a message-ingest credential shared with another scope', () => {
  assert.throws(
    () => loadConfig({
      ...validEnvironment,
      ChannelWorker__MessageIngestServiceToken: validEnvironment.ChannelWorker__ClaimServiceToken,
    }),
    /devem ser distintas/,
  )
})

test('loadConfig rejects a media-ingest credential shared with another scope', () => {
  assert.throws(
    () => loadConfig({
      ...validEnvironment,
      ChannelWorker__MediaIngestServiceToken: validEnvironment.ChannelWorker__ClaimServiceToken,
    }),
    /devem ser distintas/,
  )
})

test('loadConfig rejects a session directory inside the repository', () => {
  assert.throws(
    () => loadConfig({
      ...validEnvironment,
      ChannelWorker__SessionPath: `${process.cwd()}/session`,
    }),
    /para fora do repositório/,
  )
})

test('loadConfig rejects polling intervals over the ADR-042 maximum', () => {
  assert.throws(
    () => loadConfig({
      ...validEnvironment,
      ChannelWorker__PollingIntervalSeconds: '31',
    }),
    /entre 1 e 30/,
  )
})
