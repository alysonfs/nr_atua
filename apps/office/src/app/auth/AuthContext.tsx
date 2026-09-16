/**
 * Contexto de autenticação do Office.
 *
 * Segue ADR-004/ADR-017:
 * - JWT de acesso mantido SOMENTE em memória (nunca localStorage/sessionStorage).
 * - Refresh token trafega em cookie HttpOnly gerenciado pelo browser — o JS
 *   não o lê nem o manipula diretamente.
 * - Ao montar, tenta silenciosamente POST /auth/refresh para restaurar sessão
 *   via cookie existente. Falha tratada como "deslogado" sem erro visível.
 *
 * Em desenvolvimento, React StrictMode pode remontar efeitos; a restauração
 * de sessão precisa ser deduplicada porque refresh token é rotativo e single-use.
 */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { apiClient, setAccessTokenProvider } from '../../shared/lib/apiClient'
import { changePreferredLocale, syncAuthenticatedLocale } from '../../shared/lib/localePreference'
import type { SupportedLocale } from '../../i18n'

interface AccessTokenResponse {
  accessToken: string
}

interface AuthState {
  accessToken: string | null
  isLoading: boolean
  localeSyncError: boolean
}

interface AuthContextValue {
  isAuthenticated: boolean
  isLoading: boolean
  localeSyncError: boolean
  signIn: (email: string, password: string) => Promise<void>
  signOut: () => Promise<void>
  changeLocale: (locale: SupportedLocale) => Promise<void>
  retryLocaleSync: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

let restoreSessionPromise: Promise<AccessTokenResponse | null> | null = null

function restoreSessionOnce(): Promise<AccessTokenResponse | null> {
  restoreSessionPromise ??= apiClient
    .post<AccessTokenResponse>('/auth/refresh')
    .finally(() => {
      restoreSessionPromise = null
    })

  return restoreSessionPromise
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({
    accessToken: null,
    isLoading: true,
    localeSyncError: false,
  })

  // Ref para que setAccessTokenProvider sempre enxergue o token mais recente
  // sem precisar re-registrar o provider a cada mudança de state.
  const tokenRef = useRef<string | null>(null)

  // Registra o provider UMA VEZ na montagem. O closure lê tokenRef que
  // sempre aponta para o valor mais recente.
  useEffect(() => {
    setAccessTokenProvider(() => tokenRef.current)
  }, [])

  // Mantém ref sincronizado com state.
  useEffect(() => {
    tokenRef.current = state.accessToken
  }, [state.accessToken])

  useEffect(() => {
    let cancelled = false

    restoreSessionOnce()
      .then((data) => {
        if (!cancelled && data) {
          tokenRef.current = data.accessToken
          setState({ accessToken: data.accessToken, isLoading: false, localeSyncError: false })
          void syncAuthenticatedLocale().catch(() => {
            if (!cancelled) setState((current) => ({ ...current, localeSyncError: true }))
          })
        } else if (!cancelled) {
          setState({ accessToken: null, isLoading: false, localeSyncError: false })
        }
      })
      .catch(() => {
        if (!cancelled) {
          setState({ accessToken: null, isLoading: false, localeSyncError: false })
        }
      })

    return () => {
      cancelled = true
    }
  }, [])

  const signIn = useCallback(async (email: string, password: string) => {
    const data = await apiClient.post<AccessTokenResponse>('/auth/signin', {
      email,
      password,
    })
    if (data) {
      tokenRef.current = data.accessToken
      setState({ accessToken: data.accessToken, isLoading: false, localeSyncError: false })
      try {
        await syncAuthenticatedLocale()
      } catch {
        setState((current) => ({ ...current, localeSyncError: true }))
      }
    }
  }, [])

  const signOut = useCallback(async () => {
    try {
      await apiClient.post('/auth/signout')
    } finally {
      tokenRef.current = null
      setState({ accessToken: null, isLoading: false, localeSyncError: false })
    }
  }, [])

  const changeLocale = useCallback(async (locale: SupportedLocale) => {
    try {
      await changePreferredLocale(locale, tokenRef.current !== null)
      setState((current) => ({ ...current, localeSyncError: false }))
    } catch {
      setState((current) => ({ ...current, localeSyncError: true }))
    }
  }, [])

  const retryLocaleSync = useCallback(async () => {
    if (!tokenRef.current) return
    try {
      await syncAuthenticatedLocale()
      setState((current) => ({ ...current, localeSyncError: false }))
    } catch {
      setState((current) => ({ ...current, localeSyncError: true }))
    }
  }, [])

  return (
    <AuthContext.Provider
      value={{
        isAuthenticated: state.accessToken !== null,
        isLoading: state.isLoading,
        localeSyncError: state.localeSyncError,
        signIn,
        signOut,
        changeLocale,
        retryLocaleSync,
      }}
    >
      {children}
    </AuthContext.Provider>
  )
}

// The hook intentionally shares this module with its provider to keep the
// authentication contract and context private to one boundary.
// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth deve ser usado dentro de <AuthProvider>')
  }
  return ctx
}
