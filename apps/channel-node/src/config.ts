import { dirname, isAbsolute, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

export interface ChannelConfig {
  apiBaseUrl: string
  claimServiceToken: string
  completeServiceToken: string
  messageIngestServiceToken: string
  mediaIngestServiceToken: string
  pollingIntervalMs: number
  sendTimeoutMs: number
  sessionPath: string
  printQr: boolean
}

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..')

export function loadConfig(environment: NodeJS.ProcessEnv = process.env): ChannelConfig {
  const apiBaseUrl = required(environment, 'ChannelWorker__ApiBaseUrl')
  const apiUrl = new URL(apiBaseUrl)
  if (apiUrl.protocol !== 'http:' && apiUrl.protocol !== 'https:') {
    throw new Error('ChannelWorker__ApiBaseUrl deve usar HTTP ou HTTPS.')
  }

  const claimServiceToken = required(environment, 'ChannelWorker__ClaimServiceToken')
  const completeServiceToken = required(environment, 'ChannelWorker__CompleteServiceToken')
  const messageIngestServiceToken = required(environment, 'ChannelWorker__MessageIngestServiceToken')
  const mediaIngestServiceToken = required(environment, 'ChannelWorker__MediaIngestServiceToken')
  if (
    new Set([
      claimServiceToken,
      completeServiceToken,
      messageIngestServiceToken,
      mediaIngestServiceToken,
    ]).size !== 4
  ) {
    throw new Error('As credenciais de claim, complete, message ingest e media ingest devem ser distintas.')
  }

  const configuredSessionPath = required(environment, 'ChannelWorker__SessionPath')
  if (!isAbsolute(configuredSessionPath)) {
    throw new Error('ChannelWorker__SessionPath deve ser absoluto e externo ao repositório.')
  }

  const sessionPath = resolve(configuredSessionPath)
  const relativeSessionPath = relative(repositoryRoot, sessionPath)
  if (
    relativeSessionPath === '' ||
    (relativeSessionPath !== '..' &&
      !relativeSessionPath.startsWith(`..${sep}`) &&
      !isAbsolute(relativeSessionPath))
  ) {
    throw new Error('ChannelWorker__SessionPath deve apontar para fora do repositório.')
  }

  return {
    apiBaseUrl,
    claimServiceToken,
    completeServiceToken,
    messageIngestServiceToken,
    mediaIngestServiceToken,
    pollingIntervalMs: parseSeconds(environment, 'ChannelWorker__PollingIntervalSeconds', 15, 1, 30),
    sendTimeoutMs: parseSeconds(environment, 'ChannelWorker__SendTimeoutSeconds', 30, 1, 45),
    sessionPath,
    printQr: parseBoolean(environment, 'ChannelWorker__PrintQr', false),
  }
}

function required(environment: NodeJS.ProcessEnv, name: string): string {
  const value = environment[name]
  if (value === undefined || value.trim() === '') {
    throw new Error(`${name} é obrigatório.`)
  }

  return value.trim()
}

function parseSeconds(
  environment: NodeJS.ProcessEnv,
  name: string,
  defaultValue: number,
  minimum: number,
  maximum: number,
): number {
  const rawValue = environment[name]
  if (rawValue === undefined || rawValue.trim() === '') {
    return defaultValue * 1_000
  }

  const seconds = Number(rawValue)
  if (!Number.isInteger(seconds) || seconds < minimum || seconds > maximum) {
    throw new Error(`${name} deve ser um inteiro entre ${minimum} e ${maximum}.`)
  }

  return seconds * 1_000
}

function parseBoolean(environment: NodeJS.ProcessEnv, name: string, defaultValue: boolean): boolean {
  const value = environment[name]
  if (value === undefined || value.trim() === '') {
    return defaultValue
  }

  if (value.toLowerCase() === 'true') {
    return true
  }

  if (value.toLowerCase() === 'false') {
    return false
  }

  throw new Error(`${name} deve ser true ou false.`)
}
