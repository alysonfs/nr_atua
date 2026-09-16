/**
 * Test suite for AppLayout
 *
 * Verifica que o layout padrão do Office mantém:
 * - Header sempre visível no topo
 * - Sidebar fixo à esquerda (estrutural, sem itens de navegação ainda)
 * - Miolo renderizando a rota ativa via <Outlet />
 */

import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { AppLayout } from '../../components/Layout/AppLayout'

vi.mock('../../../auth/AuthContext', () => ({
  useAuth: vi.fn(() => ({
    isAuthenticated: true,
    isLoading: false,
    signIn: vi.fn(),
    signOut: vi.fn(),
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

function renderAppLayout(initialPath = '/home') {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route element={<AppLayout />}>
          <Route path="/home" element={<div>Conteúdo do dashboard</div>} />
          <Route path="/settings" element={<div>Conteúdo de configurações</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('AppLayout', () => {
  it('renders the header, sidebar and the outlet content together', () => {
    renderAppLayout('/home')

    // Header sempre visível
    expect(screen.getByRole('link', { name: /configurações/i })).toBeInTheDocument()

    // Sidebar estrutural, sem itens de navegação ainda
    expect(screen.getByRole('navigation', { name: /navegação principal/i })).toBeInTheDocument()

    // Miolo renderiza a rota ativa
    expect(screen.getByText('Conteúdo do dashboard')).toBeInTheDocument()
  })

  it('renders the settings route content in the same layout', () => {
    renderAppLayout('/settings')

    expect(screen.getByRole('link', { name: /configurações/i })).toBeInTheDocument()
    expect(screen.getByText('Conteúdo de configurações')).toBeInTheDocument()
  })
})
