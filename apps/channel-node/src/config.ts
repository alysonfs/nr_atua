import { dirname, isAbsolute, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

export interface ChannelConfig {
  apiBaseUrl: string
  claimServiceToken: string
  completeServiceToken: string
  messageIngestServiceToken: string
  mediaIngestServiceToken: string
  /**
   * Credencial de serviço para reportar o estado de pareamento (QR) à API
   * (ADR-049). Opcional: se ausente, o Channel segue operando normalmente
   * — apenas não reporta o QR ao Office, registrando um aviso único.
   */
  sessionReportServiceToken: string | undefined
  pollingIntervalMs: number
  sendTimeoutMs: number
  sessionPath: string
  printQr: boolean
  /** Capacidade máxima da fila inbound de mídia (R1) — descarta com log acima deste limite. */
  mediaQueueCapacity: number
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
  const sessionReportServiceToken = optional(environment, 'ChannelWorker__SessionReportServiceToken')
  const distinctTokens = [
    claimServiceToken,
    completeServiceToken,
    messageIngestServiceToken,
    mediaIngestServiceToken,
    ...(sessionReportServiceToken === undefined ? [] : [sessionReportServiceToken]),
  ]
  if (new Set(distinctTokens).size !== distinctTokens.length) {
    throw new Error(
      'As credenciais de claim, complete, message ingest, media ingest e session report devem ser distintas.',
    )
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
    sessionReportServiceToken,
    pollingIntervalMs: parseSeconds(environment, 'ChannelWorker__PollingIntervalSeconds', 15, 1, 30),
    sendTimeoutMs: parseSeconds(environment, 'ChannelWorker__SendTimeoutSeconds', 30, 1, 45),
    sessionPath,
    printQr: parseBoolean(environment, 'ChannelWorker__PrintQr', false),
    mediaQueueCapacity: parseInteger(environment, 'ChannelWorker__MediaQueueCapacity', 20, 1, 1_000),
  }
}

function required(environment: NodeJS.ProcessEnv, name: string): string {
  const value = environment[name]
  if (value === undefined || value.trim() === '') {
    throw new Error(`${name} é obrigatório.`)
  }

  return value.trim()
}

function optional(environment: NodeJS.ProcessEnv, name: string): string | undefined {
  const value = environment[name]
  if (value === undefined || value.trim() === '') {
    return undefined
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

function parseInteger(
  environment: NodeJS.ProcessEnv,
  name: string,
  defaultValue: number,
  minimum: number,
  maximum: number,
): number {
  const rawValue = environment[name]
  if (rawValue === undefined || rawValue.trim() === '') {
    return defaultValue
  }

  const value = Number(rawValue)
  if (!Number.isInteger(value) || value < minimum || value > maximum) {
    throw new Error(`${name} deve ser um inteiro entre ${minimum} e ${maximum}.`)
  }

  return value
}
