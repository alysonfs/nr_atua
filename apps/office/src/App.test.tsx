import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import App from './App'

// App agora usa useAuth (para o botão de logout); mocamos para evitar
// dependência do AuthProvider nos testes de integração do dashboard.
vi.mock('./app/auth/AuthContext', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    isLoading: false,
    signIn: vi.fn(),
    signOut: vi.fn(),
  }),
}))

vi.mock('./app/office/hooks/useUserTrial', () => ({
  useUserTrial: () => ({
    trial: {
      trialId: 'trial-test',
      activatedAtUtc: '2026-08-22T00:00:00.000Z',
      expiresAtUtc: '2026-08-29T00:00:00.000Z',
      status: 'active' as const,
      daysRemaining: 7,
    },
    summary: {
      isActive: true,
      isExpired: false,
      daysRemaining: 7,
      hoursRemaining: 168,
      isAboutToExpire: false,
      isUrgent: false,
    },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}))

vi.mock('./app/office/hooks/useTimezone', () => ({
  useTimezone: () => ({
    currentTimezone: 'UTC',
    formatDateLocal: () => '29/08/2026',
    updateTimezone: vi.fn(),
    resetToSuggested: vi.fn(),
    getCurrentTimezone: () => 'UTC',
    isLoading: false,
    error: null,
  }),
}))

vi.mock('./app/office/hooks/useTenants', () => ({
  useMyTenants: () => ({
    tenants: [
      { tenantId: 'tenant-1', name: 'Empresa Teste', role: 'OWNER', integrationId: 'integration-1' },
    ],
    defaultTenantId: 'tenant-1',
    hasNoTenant: false,
    requiresSelection: false,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
  useCreateTenant: () => ({
    createTenant: vi.fn(),
    isSubmitting: false,
  }),
}))


vi.mock('./app/office/hooks/useIServiceIntegration', () => ({
  useIServiceCredentials: () => ({
    status: {
      hasCredentials: false,
      validationStatus: 'NotValidated',
      lastValidatedAtUtc: null,
      updatedAtUtc: null,
    },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
  useSetIServiceCredentials: () => ({
    setCredentials: vi.fn(),
    isSubmitting: false,
  }),
  useValidateIServiceCredentials: () => ({
    validate: vi.fn(),
    isValidating: false,
  }),
}))

describe('App', () => {
  it('renders the Trial and timezone preference sections', () => {
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Seu Trial' })).toBeInTheDocument()
    expect(screen.getByText('Trial Ativo')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Preferências' })).toBeInTheDocument()
    expect(screen.getByText('Fuso Horário')).toBeInTheDocument()
  })

  it('renderiza o painel de integração com o iService usando o integrationId real vindo do backend (BUG-RF006-001)', () => {
    render(<App />)

    expect(
      screen.getByRole('heading', { name: 'Integração com o iService' }),
    ).toBeInTheDocument()
    // O painel só é renderizado quando há tenantId + integrationId reais
    // (sem mock de useIServiceIntegrationId), confirmando que App.tsx
    // deriva o integrationId de useMyTenants().
    expect(screen.queryByText('Preparando a configuração da integração...')).not.toBeInTheDocument()
  })
})
