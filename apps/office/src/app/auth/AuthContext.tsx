/**
 * Contexto de autenticação do Office.
 *
 * Segue ADR-004/ADR-017:
 * - JWT de acesso mantido SOMENTE em memória (nunca localStorage/sessionStorage).
 * - Refresh token trafega em cookie HttpOnly gerenciado pelo browser — o JS
 *   não o lê nem o manipula diretamente.
 * - Ao montar, tenta silenciosamente POST /auth/refresh para restaurar sessão
 *   via cookie existente. Falha tratada como "deslogado" sem erro visível.
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

interface AccessTokenResponse {
  accessToken: string
}

interface AuthState {
  accessToken: string | null
  isLoading: boolean
}

interface AuthContextValue {
  isAuthenticated: boolean
  isLoading: boolean
  signIn: (email: string, password: string) => Promise<void>
  signOut: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({
    accessToken: null,
    isLoading: true,
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

  // Tentativa silenciosa de restaurar sessão ao montar.
  useEffect(() => {
    let cancelled = false

    apiClient
      .post<AccessTokenResponse>('/auth/refresh')
      .then((data) => {
        if (!cancelled && data) {
          setState({ accessToken: data.accessToken, isLoading: false })
        } else if (!cancelled) {
          setState({ accessToken: null, isLoading: false })
        }
      })
      .catch(() => {
        if (!cancelled) {
          setState({ accessToken: null, isLoading: false })
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
      setState({ accessToken: data.accessToken, isLoading: false })
    }
  }, [])

  const signOut = useCallback(async () => {
    try {
      await apiClient.post('/auth/signout')
    } finally {
      setState({ accessToken: null, isLoading: false })
    }
  }, [])

  return (
    <AuthContext.Provider
      value={{
        isAuthenticated: state.accessToken !== null,
        isLoading: state.isLoading,
        signIn,
        signOut,
      }}
    >
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth deve ser usado dentro de <AuthProvider>')
  }
  return ctx
}
