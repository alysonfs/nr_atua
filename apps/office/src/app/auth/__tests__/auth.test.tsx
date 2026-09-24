/**
 * Testes para as páginas de autenticação e rota protegida.
 *
 * Segue o mesmo padrão dos testes existentes em
 * apps/office/src/app/office/__tests__/ (vitest + @testing-library/react).
 */

import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { SignInPage } from '../pages/SignInPage'
import { SignUpPage } from '../pages/SignUpPage'
import { ConfirmEmailPage } from '../pages/ConfirmEmailPage'
import { ProtectedRoute } from '../ProtectedRoute'
import { ApiError } from '../../../shared/lib/apiClient'

// ──────────────────────────────────────────────────────────────────────────────
// Mocks de módulos (nível de topo — hoistados pelo vitest)
// ──────────────────────────────────────────────────────────────────────────────

const mockSignIn = vi.fn()
const mockSignOut = vi.fn()
// Usamos objetos mutáveis para controlar o estado por teste
const mockAuthState = { isAuthenticated: false, isLoading: false }

vi.mock('../AuthContext', () => ({
  useAuth: () => ({
    isAuthenticated: mockAuthState.isAuthenticated,
    isLoading: mockAuthState.isLoading,
    signIn: mockSignIn,
    signOut: mockSignOut,
  }),
}))

const mockPostFn = vi.fn()

vi.mock('../../../shared/lib/apiClient', async () => {
  const actual =
    await vi.importActual<typeof import('../../../shared/lib/apiClient')>(
      '../../../shared/lib/apiClient',
    )
  return {
    ...actual,
    apiClient: {
      get: vi.fn(),
      post: (...args: unknown[]) => mockPostFn(...args),
      put: vi.fn(),
    },
  }
})

// Mocks dos hooks do dashboard (usados pelo teste de logout via App)
vi.mock('../../office/hooks/useUserTrial', () => ({
  useUserTrial: () => ({
    trial: null,
    summary: null,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}))

vi.mock('../../office/hooks/useTimezone', () => ({
  useTimezone: () => ({
    currentTimezone: 'UTC',
    formatDateLocal: () => '',
    updateTimezone: vi.fn(),
    resetToSuggested: vi.fn(),
    getCurrentTimezone: () => 'UTC',
    isLoading: false,
    error: null,
  }),
}))

vi.mock('../../office/hooks/useTenants', () => ({
  useMyTenants: () => ({
    tenants: [],
    defaultTenantId: null,
    hasNoTenant: true,
    requiresSelection: false,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
  useCreateTenant: () => ({ createTenant: vi.fn(), isSubmitting: false }),
}))

vi.mock('../../office/hooks/useIServiceIntegration', () => ({
  useIServiceCredentials: () => ({
    status: { hasCredentials: false, validationStatus: 'NotValidated', lastValidatedAtUtc: null, updatedAtUtc: null },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
  useSetIServiceCredentials: () => ({ setCredentials: vi.fn(), isSubmitting: false }),
  useValidateIServiceCredentials: () => ({ validate: vi.fn(), isValidating: false }),
}))

// ──────────────────────────────────────────────────────────────────────────────
// Helpers de renderização
// ──────────────────────────────────────────────────────────────────────────────

function renderSignIn(initialEntries = ['/login']) {
  return render(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/login" element={<SignInPage />} />
        <Route path="/" element={<div>Dashboard</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

function renderSignUp(initialEntries = ['/cadastro']) {
  return render(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/cadastro" element={<SignUpPage />} />
        <Route path="/confirmar-email" element={<ConfirmEmailPage />} />
        <Route path="/login" element={<div>Login</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

function renderConfirmEmail(initialEntries = ['/confirmar-email']) {
  return render(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/confirmar-email" element={<ConfirmEmailPage />} />
        <Route path="/login" element={<div>Login</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

// ──────────────────────────────────────────────────────────────────────────────
// SignIn
// ──────────────────────────────────────────────────────────────────────────────

describe('SignInPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('renderiza o formulário de login', () => {
    renderSignIn()
    expect(screen.getByLabelText(/e-mail/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/senha/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /entrar/i })).toBeInTheDocument()
  })

  it('chama signIn e redireciona para o dashboard em caso de sucesso', async () => {
    mockSignIn.mockResolvedValueOnce(undefined)
    renderSignIn()

    await userEvent.type(screen.getByLabelText(/e-mail/i), 'joao@example.com')
    await userEvent.type(screen.getByLabelText(/senha/i), 'Senha123!')
    await userEvent.click(screen.getByRole('button', { name: /entrar/i }))

    await waitFor(() => {
      expect(mockSignIn).toHaveBeenCalledWith('joao@example.com', 'Senha123!')
    })

    await waitFor(() => {
      expect(screen.getByText('Dashboard')).toBeInTheDocument()
    })
  })

  it('exibe mensagem genérica para erro 401 (RN-005.2 — não diferencia e-mail/senha errada)', async () => {
    mockSignIn.mockRejectedValueOnce(new ApiError(401, undefined, 'Unauthorized'))
    renderSignIn()

    await userEvent.type(screen.getByLabelText(/e-mail/i), 'joao@example.com')
    await userEvent.type(screen.getByLabelText(/senha/i), 'senhaerrada')
    await userEvent.click(screen.getByRole('button', { name: /entrar/i }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/e-mail ou senha inválidos/i)
    })
  })

  it('exibe mensagem de erro genérica para falhas de rede inesperadas', async () => {
    mockSignIn.mockRejectedValueOnce(new Error('network error'))
    renderSignIn()

    await userEvent.type(screen.getByLabelText(/e-mail/i), 'joao@example.com')
    await userEvent.type(screen.getByLabelText(/senha/i), 'qualquersenha')
    await userEvent.click(screen.getByRole('button', { name: /entrar/i }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/não foi possível entrar/i)
    })
  })
})

// ──────────────────────────────────────────────────────────────────────────────
// SignUp
// ──────────────────────────────────────────────────────────────────────────────

describe('SignUpPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('renderiza o formulário de cadastro', () => {
    renderSignUp()
    expect(screen.getByLabelText(/^e-mail/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/^senha$/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/confirmar senha/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /criar conta/i })).toBeInTheDocument()
  })

  it('sucesso (202 Accepted): redireciona para /confirmar-email com mensagem de sucesso', async () => {
    mockPostFn.mockResolvedValueOnce(null)
    renderSignUp()

    await userEvent.type(screen.getByLabelText(/^e-mail/i), 'maria@example.com')
    await userEvent.type(screen.getByLabelText(/^senha$/i), 'Senha123!')
    await userEvent.type(screen.getByLabelText(/confirmar senha/i), 'Senha123!')
    await userEvent.click(screen.getByRole('button', { name: /criar conta/i }))

    await waitFor(() => {
      expect(screen.getByText(/confirme seu e-mail/i)).toBeInTheDocument()
    })
    expect(screen.getByText(/verifique sua caixa de entrada/i)).toBeInTheDocument()
  })

  it('erro 409 email_already_registered: exibe mensagem específica', async () => {
    mockPostFn.mockRejectedValueOnce(
      new ApiError(409, 'email_already_registered', 'email_already_registered'),
    )
    renderSignUp()

    await userEvent.type(screen.getByLabelText(/^e-mail/i), 'maria@example.com')
    await userEvent.type(screen.getByLabelText(/^senha$/i), 'Senha123!')
    await userEvent.type(screen.getByLabelText(/confirmar senha/i), 'Senha123!')
    await userEvent.click(screen.getByRole('button', { name: /criar conta/i }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/já está cadastrado/i)
    })
  })

  it('senha muito curta: exibe erro no campo sem chamar a API', async () => {
    renderSignUp()

    await userEvent.type(screen.getByLabelText(/^e-mail/i), 'maria@example.com')
    await userEvent.type(screen.getByLabelText(/^senha$/i), '123')
    await userEvent.type(screen.getByLabelText(/confirmar senha/i), '123')
    await userEvent.click(screen.getByRole('button', { name: /criar conta/i }))

    await waitFor(() => {
      expect(screen.getByText(/mínimo 8 caracteres/i)).toBeInTheDocument()
    })
    expect(mockPostFn).not.toHaveBeenCalled()
  })

  it('erro 400 invalid_password: exibe mensagem de senha inválida', async () => {
    mockPostFn.mockRejectedValueOnce(new ApiError(400, 'invalid_password', 'invalid_password'))
    renderSignUp()

    await userEvent.type(screen.getByLabelText(/^e-mail/i), 'maria@example.com')
    await userEvent.type(screen.getByLabelText(/^senha$/i), 'senhasenha')
    await userEvent.type(screen.getByLabelText(/confirmar senha/i), 'senhasenha')
    await userEvent.click(screen.getByRole('button', { name: /criar conta/i }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/senha inválida/i)
    })
  })

  it('senhas não coincidem: exibe erro no campo sem chamar a API', async () => {
    renderSignUp()

    await userEvent.type(screen.getByLabelText(/^e-mail/i), 'maria@example.com')
    await userEvent.type(screen.getByLabelText(/^senha$/i), 'Senha123!')
    await userEvent.type(screen.getByLabelText(/confirmar senha/i), 'DiferenteSenha!')
    await userEvent.click(screen.getByRole('button', { name: /criar conta/i }))

    await waitFor(() => {
      expect(screen.getByText(/não coincidem/i)).toBeInTheDocument()
    })
    expect(mockPostFn).not.toHaveBeenCalled()
  })
})

// ──────────────────────────────────────────────────────────────────────────────
// ConfirmEmail
// ──────────────────────────────────────────────────────────────────────────────

describe('ConfirmEmailPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('renderiza o formulário de confirmação', () => {
    renderConfirmEmail()
    expect(screen.getByLabelText(/e-mail/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/código de confirmação/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /confirmar e-mail/i })).toBeInTheDocument()
  })

  it('sucesso: redireciona para /login', async () => {
    mockPostFn.mockResolvedValueOnce(null)
    renderConfirmEmail()

    await userEvent.type(screen.getByLabelText(/e-mail/i), 'joao@example.com')
    await userEvent.type(screen.getByLabelText(/código de confirmação/i), '123456')
    await userEvent.click(screen.getByRole('button', { name: /confirmar e-mail/i }))

    await waitFor(() => {
      expect(screen.getByText('Login')).toBeInTheDocument()
    })
  })

  it('erro 400 invalid_code: exibe mensagem de código inválido', async () => {
    mockPostFn.mockRejectedValueOnce(new ApiError(400, 'invalid_code', 'invalid_code'))
    renderConfirmEmail()

    await userEvent.type(screen.getByLabelText(/e-mail/i), 'joao@example.com')
    await userEvent.type(screen.getByLabelText(/código de confirmação/i), '000000')
    await userEvent.click(screen.getByRole('button', { name: /confirmar e-mail/i }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/código inválido/i)
    })
  })

  it('erro 400 expired_code: exibe mensagem de código expirado', async () => {
    mockPostFn.mockRejectedValueOnce(new ApiError(400, 'expired_code', 'expired_code'))
    renderConfirmEmail()

    await userEvent.type(screen.getByLabelText(/e-mail/i), 'joao@example.com')
    await userEvent.type(screen.getByLabelText(/código de confirmação/i), '111111')
    await userEvent.click(screen.getByRole('button', { name: /confirmar e-mail/i }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/código expirado/i)
    })
  })

  it('erro 409 email_already_confirmed: exibe mensagem específica', async () => {
    mockPostFn.mockRejectedValueOnce(
      new ApiError(409, 'email_already_confirmed', 'email_already_confirmed'),
    )
    renderConfirmEmail()

    await userEvent.type(screen.getByLabelText(/e-mail/i), 'joao@example.com')
    await userEvent.type(screen.getByLabelText(/código de confirmação/i), '999999')
    await userEvent.click(screen.getByRole('button', { name: /confirmar e-mail/i }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/já foi confirmado/i)
    })
  })
})

// ──────────────────────────────────────────────────────────────────────────────
// ProtectedRoute
// ──────────────────────────────────────────────────────────────────────────────

describe('ProtectedRoute', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('redireciona para /login quando não autenticado e não está carregando', () => {
    mockAuthState.isAuthenticated = false
    mockAuthState.isLoading = false

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<ProtectedRoute />}>
            <Route path="/" element={<div>Dashboard</div>} />
          </Route>
          <Route path="/login" element={<div>Página de Login</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Página de Login')).toBeInTheDocument()
    expect(screen.queryByText('Dashboard')).not.toBeInTheDocument()
  })

  it('renderiza o dashboard quando autenticado', () => {
    mockAuthState.isAuthenticated = true
    mockAuthState.isLoading = false

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<ProtectedRoute />}>
            <Route path="/" element={<div>Dashboard</div>} />
          </Route>
          <Route path="/login" element={<div>Página de Login</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Dashboard')).toBeInTheDocument()
    expect(screen.queryByText('Página de Login')).not.toBeInTheDocument()
  })

  it('exibe indicador de carregamento enquanto isLoading é true', () => {
    mockAuthState.isAuthenticated = false
    mockAuthState.isLoading = true

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<ProtectedRoute />}>
            <Route path="/" element={<div>Dashboard</div>} />
          </Route>
          <Route path="/login" element={<div>Página de Login</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByLabelText(/carregando/i)).toBeInTheDocument()
    expect(screen.queryByText('Dashboard')).not.toBeInTheDocument()
    expect(screen.queryByText('Página de Login')).not.toBeInTheDocument()
  })
})

// ──────────────────────────────────────────────────────────────────────────────
// Logout — botão Sair no header (AppLayout)
// ──────────────────────────────────────────────────────────────────────────────

vi.mock('../../office/hooks/useUserTrial', () => ({
  useUserTrial: () => ({
    trial: null,
    summary: null,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}))

describe('Logout (botão Sair no header)', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    mockAuthState.isAuthenticated = true
    mockAuthState.isLoading = false
  })

  it('chama signOut quando o botão Sair é clicado', async () => {
    mockSignOut.mockResolvedValueOnce(undefined)
    const { AppLayout } = await import('../../office/components/Layout')

    render(
      <MemoryRouter initialEntries={['/home']}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/home" element={<div>Dashboard</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    )

    const btnMenuConta = screen.getByRole('button', { name: /abrir menu da conta/i })
    await userEvent.click(btnMenuConta)

    const btnSair = screen.getByRole('menuitem', { name: /sair da conta/i })
    await userEvent.click(btnSair)

    await waitFor(() => {
      expect(mockSignOut).toHaveBeenCalledTimes(1)
    })
  })
})
