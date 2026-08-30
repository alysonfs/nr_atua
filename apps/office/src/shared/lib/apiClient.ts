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

async function request<TResponse>(
  path: string,
  init: RequestInit = {},
): Promise<TResponse | null> {
  const token = accessTokenProvider?.() ?? null
  const headers = new Headers(init.headers)
  headers.set('Content-Type', 'application/json')
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const response = await fetch(path, {
    ...init,
    headers,
    credentials: 'include',
  })

  if (response.status === 204) {
    return null
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
  put: <TResponse>(path: string, body?: unknown) =>
    request<TResponse>(path, { method: 'PUT', body: body ? JSON.stringify(body) : undefined }),
}
