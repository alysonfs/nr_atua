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
  /**
   * Atrasos (em ms) em que uma busca antecipada de comandos (claim) é
   * disparada após o ingest bem-sucedido de uma mensagem inbound — reduz a
   * latência entre a resposta da IA virar comando na outbox e o envio pelo
   * Channel, sem substituir o polling normal (que continua como
   * recuperação). Configurado via `ChannelWorker__EarlyClaimDelaysSeconds`
   * (lista de inteiros em segundos, separados por vírgula).
   */
  earlyClaimDelaysMs: number[]
}

/** Atrasos padrão (em ms) das buscas antecipadas de comandos: imediato, 3s, 6s e 10s após o ingest. */
export const DEFAULT_EARLY_CLAIM_DELAYS_MS = [0, 3_000, 6_000, 10_000]

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
    earlyClaimDelaysMs: parseDelayListSeconds(
      environment,
      'ChannelWorker__EarlyClaimDelaysSeconds',
      DEFAULT_EARLY_CLAIM_DELAYS_MS,
      0,
      30,
      10,
    ),
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

/**
 * Lista de atrasos (segundos, separados por vírgula) convertida para ms,
 * ordenada e deduplicada — usada pelas buscas antecipadas de comandos
 * (claim) agendadas após o ingest de uma mensagem inbound. Uma lista vazia
 * (`""`) desativa o recurso: nenhuma busca antecipada é agendada e o
 * comportamento cai de volta ao polling normal.
 */
function parseDelayListSeconds(
  environment: NodeJS.ProcessEnv,
  name: string,
  defaultValuesMs: number[],
  minimumSeconds: number,
  maximumSeconds: number,
  maximumEntries: number,
): number[] {
  const rawValue = environment[name]
  if (rawValue === undefined || rawValue.trim() === '') {
    return defaultValuesMs
  }

  if (rawValue.trim() === '-') {
    return []
  }

  const parts = rawValue
    .split(',')
    .map((part) => part.trim())
    .filter((part) => part !== '')

  if (parts.length === 0 || parts.length > maximumEntries) {
    throw new Error(`${name} deve conter entre 1 e ${maximumEntries} valores inteiros separados por vírgula.`)
  }

  const seconds = parts.map((part) => {
    const value = Number(part)
    if (!Number.isInteger(value) || value < minimumSeconds || value > maximumSeconds) {
      throw new Error(`${name} deve conter inteiros entre ${minimumSeconds} e ${maximumSeconds} segundos.`)
    }
    return value
  })

  return [...new Set(seconds)].sort((a, b) => a - b).map((value) => value * 1_000)
}
