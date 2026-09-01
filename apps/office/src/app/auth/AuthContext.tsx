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
 * LIMITAÇÃO TEMPORÁRIA (débito técnico — ADR proposto por Ari/aws-architect):
 * No cenário de deploy cross-origin sem HTTPS (Office no S3 + API na EC2 sem
 * domínio próprio), o cookie HttpOnly de refresh NÃO é enviado pelo browser na
 * requisição POST /auth/refresh (política SameSite + ausência de Secure).
 * Por isso, a tentativa de restauração silenciosa de sessão ao montar sempre
 * falhará nesse ambiente — o usuário precisará fazer login manualmente após
 * cada expiração do access token (~15 min).
 * A renovação automática será reativada quando houver domínio + HTTPS
 * configurados (ambas as origens sob o mesmo domínio ou CORS com credenciais).
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
  //
  // NOTA: Em deploy cross-origin sem HTTPS (S3 + EC2), o cookie de refresh não
  // chega ao servidor — a chamada retorna 401 e o catch abaixo garante logout
  // limpo (accessToken=null, isLoading=false), sem loop ou erro não tratado.
  // Esse é o comportamento esperado enquanto não houver domínio + HTTPS.
  // Ref.: ADR proposto (débito técnico) — ver comentário no topo deste arquivo.
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
