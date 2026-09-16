/**
 * Test suite for SettingsPage
 *
 * Verifica que o rótulo visível da página é "Configurações" (troca de
 * "Painel operacional"), mantendo a rota /settings.
 */

import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SettingsPage } from '../../pages/SettingsPage'

vi.mock('../../hooks/useTenants', () => ({
  useMyTenants: vi.fn(() => ({
    tenants: [],
    defaultTenantId: null,
    hasNoTenant: false,
    requiresSelection: false,
    isLoading: true,
    isError: false,
    refetch: vi.fn(),
  })),
}))

vi.mock('../../hooks/useUserTrial', () => ({
  useUserTrial: vi.fn(() => ({
    trial: null,
    summary: null,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  })),
}))

vi.mock('../../hooks/useTimezone', () => ({
  useTimezone: vi.fn(() => ({
    currentTimezone: 'America/Sao_Paulo',
    updateTimezone: vi.fn(),
    isLoading: false,
    error: null,
    formatDateLocal: (date: string) => new Date(date).toLocaleDateString('pt-BR'),
  })),
}))

describe('SettingsPage', () => {
  it('renders "Configurações" as the visible page title', () => {
    render(<SettingsPage />)

    expect(screen.getByRole('heading', { level: 1, name: 'Configurações' })).toBeInTheDocument()
    expect(screen.queryByText('Painel operacional')).not.toBeInTheDocument()
  })
})
