/**
 * Cliente HTTP mínimo para o Office.
 *
 * Segue o contrato de sessão já definido (ADR-004/ADR-017): o JWT de acesso é
 * mantido em memória no browser e enviado via header Authorization; o
 * refresh token trafega em cookie HttpOnly gerenciado pelo próprio browser
 * (não é responsabilidade deste cliente lê-lo ou manipulá-lo).
 *
 * Este módulo não inventa nenhuma lógica de autenticação nova: apenas
 * centraliza o envio do header e o parse de erro padronizado das APIs
 * (`{ error: string }`), evitando duplicação em cada hook de integração.
 *
 * URL base configurável:
 * Defina VITE_API_BASE_URL no build para apontar para um servidor de API em
 * origem diferente (ex.: deploy do Office no S3 contra API na EC2).
 * Sem essa variável, os paths continuam relativos (comportamento padrão de dev).
 * Ex.: VITE_API_BASE_URL=http://56.124.76.132
 */

export interface ApiErrorPayload {
  error?: string
}

export class ApiError extends Error {
  readonly status: number
  readonly code: string | undefined

  constructor(status: number, code: string | undefined, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }
}

let accessTokenProvider: (() => string | null) | null = null

/**
 * Permite que a camada de autenticação (fora do escopo desta tarefa)
 * registre como obter o JWT de acesso atual em memória.
 */
export function setAccessTokenProvider(provider: () => string | null): void {
  accessTokenProvider = provider
}

let sessionRefreshHandler: (() => Promise<boolean>) | null = null

/**
 * Permite que a camada de autenticação registre uma tentativa de renovação
 * silenciosa da sessão (via cookie HttpOnly de refresh) quando uma requisição
 * autenticada recebe 401 — o JWT de acesso tem vida curta (~15 min) e pode
 * expirar em uso normal (ex.: usuário demora para preencher um formulário).
 *
 * Retorna `true` se a sessão foi renovada (a requisição original deve ser
 * repetida com o novo token) ou `false` se a renovação falhou (sessão
 * encerrada; a camada de autenticação já deve ter tratado o logout).
 */
export function setSessionRefreshHandler(handler: (() => Promise<boolean>) | null): void {
  sessionRefreshHandler = handler
}

const NO_REFRESH_RETRY_PATHS = new Set(['/auth/refresh', '/auth/signin'])

/**
 * Resolve a URL final da requisição.
 *
 * Se VITE_API_BASE_URL estiver definida (cenário cross-origin: S3 + EC2),
 * o path é prefixado com ela. Barras duplicadas na junção são normalizadas.
 * Sem a variável (dev local ou build padrão), o path permanece relativo.
 */
function resolveUrl(path: string): string {
  const base = import.meta.env.VITE_API_BASE_URL as string | undefined
  if (!base) return path
  // Normaliza: remove trailing slash da base e garante leading slash no path.
  const normalizedBase = base.replace(/\/$/, '')
  const normalizedPath = path.startsWith('/') ? path : `/${path}`
  return `${normalizedBase}${normalizedPath}`
}

/**
 * Modo de credentials do fetch.
 *
 * Em desenvolvimento local, `localhost:5175` -> `localhost:5240` precisa de
 * 'include' para enviar o cookie HttpOnly de refresh. Em produção,
 * `office.atyno.com.br` -> `api.atyno.com.br` também precisa de 'include' para
 * aceitar e enviar o cookie seguro emitido pela API.
 */
function resolveCredentialsMode(): RequestCredentials {
  const base = import.meta.env.VITE_API_BASE_URL as string | undefined
  if (!base) return 'include'

  const url = new URL(base, window.location.origin)
  return url.protocol === 'https:' || url.hostname === 'localhost' || url.hostname === '127.0.0.1'
    ? 'include'
    : 'omit'
}

async function request<TResponse>(
  path: string,
  init: RequestInit = {},
  isRetryAfterRefresh = false,
): Promise<TResponse | null> {
  const token = accessTokenProvider?.() ?? null
  const headers = new Headers(init.headers)
  headers.set('Content-Type', 'application/json')
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const response = await fetch(resolveUrl(path), {
    ...init,
    headers,
    credentials: resolveCredentialsMode(),
  })

  if (response.status === 204) {
    return null
  }

  if (
    response.status === 401 &&
    !isRetryAfterRefresh &&
    !NO_REFRESH_RETRY_PATHS.has(path) &&
    sessionRefreshHandler
  ) {
    const refreshed = await sessionRefreshHandler()
    if (refreshed) {
      return request<TResponse>(path, init, true)
    }
  }

  const isJson = response.headers.get('content-type')?.includes('application/json')
  const body = isJson ? await response.json().catch(() => null) : null

  if (!response.ok) {
    const errorBody = body as ApiErrorPayload | null
    throw new ApiError(response.status, errorBody?.error, errorBody?.error ?? response.statusText)
  }

  return body as TResponse
}

export const apiClient = {
  get: <TResponse>(path: string) => request<TResponse>(path, { method: 'GET' }),
  post: <TResponse>(path: string, body?: unknown) =>
    request<TResponse>(path, { method: 'POST', body: body ? JSON.stringify(body) : undefined }),
  put: <TResponse>(path: string, body?: unknown, headers?: Record<string, string>) =>
    request<TResponse>(path, {
      method: 'PUT',
      body: body ? JSON.stringify(body) : undefined,
      headers,
    }),
}
