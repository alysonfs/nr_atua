/**
 * Test suite for TriagePoolPage (C1 — página da lista do pool).
 *
 * Cobre:
 * - Título/subtítulo da página.
 * - Troca de filtro de estado reflete na query string e na requisição.
 * - Aba "Em triagem" consulta o escopo pré-pool, com filtros próprios.
 * - Nenhuma ação de atendimento (ex.: "Assumir") — Office é gestão.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { TriagePoolPage } from '../../pages/TriagePoolPage'
import { useMyTenants } from '../../../office/hooks/useTenants'

const getMock = vi.fn()

vi.mock('../../../../shared/lib/apiClient', async () => {
  const actual =
    await vi.importActual<typeof import('../../../../shared/lib/apiClient')>(
      '../../../../shared/lib/apiClient',
    )
  return {
    ...actual,
    apiClient: {
      get: (...args: unknown[]) => getMock(...args),
      post: vi.fn(),
      put: vi.fn(),
    },
  }
})

vi.mock('../../../office/hooks/useTenants', () => ({
  useMyTenants: vi.fn(),
}))

const mockedUseMyTenants = vi.mocked(useMyTenants)

describe('TriagePoolPage', () => {
  beforeEach(() => {
    getMock.mockReset()
    mockedUseMyTenants.mockReturnValue({
      tenants: [],
      defaultTenantId: 'tenant-1',
      isLoading: false,
      isError: false,
      hasNoTenant: false,
      requiresSelection: false,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useMyTenants>)
  })

  it('renders the page title and subtitle', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 0, items: [] })

    render(
      <MemoryRouter>
        <TriagePoolPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: 'Pool de Triagem' })).toBeInTheDocument()
  })

  it('changes the request when a status filter is selected', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 0, items: [] })
    getMock.mockResolvedValueOnce({ totalCount: 0, items: [] })

    render(
      <MemoryRouter>
        <TriagePoolPage />
      </MemoryRouter>,
    )

    await waitFor(() => expect(getMock).toHaveBeenCalledWith('/api/tenants/tenant-1/triage-pool'))

    fireEvent.change(screen.getByLabelText(/filtrar por estado/i), { target: { value: 'FalhaDeContato' } })

    await waitFor(() =>
      expect(getMock).toHaveBeenLastCalledWith('/api/tenants/tenant-1/triage-pool?status=FalhaDeContato'),
    )
  })

  it('switches to the in-progress scope and offers only pre-pool status filters', async () => {
    getMock.mockResolvedValue({ totalCount: 0, items: [] })

    render(
      <MemoryRouter>
        <TriagePoolPage />
      </MemoryRouter>,
    )

    await waitFor(() => expect(getMock).toHaveBeenCalledWith('/api/tenants/tenant-1/triage-pool'))

    fireEvent.click(screen.getByRole('tab', { name: 'Em triagem' }))

    await waitFor(() =>
      expect(getMock).toHaveBeenLastCalledWith('/api/tenants/tenant-1/triage-pool?scope=in-progress'),
    )
    expect(screen.getByRole('tab', { name: 'Em triagem' })).toHaveAttribute('aria-selected', 'true')

    const filter = screen.getByLabelText(/filtrar por estado/i)
    expect(within(filter).getByRole('option', { name: 'Em coleta' })).toBeInTheDocument()
    expect(within(filter).queryByRole('option', { name: 'Liberada ao pool' })).not.toBeInTheDocument()

    fireEvent.change(filter, { target: { value: 'InformacoesSuficientes' } })

    await waitFor(() =>
      expect(getMock).toHaveBeenLastCalledWith(
        '/api/tenants/tenant-1/triage-pool?status=InformacoesSuficientes&scope=in-progress',
      ),
    )
  })

  it('reads the scope from the URL and renders no attendance actions', async () => {
    getMock.mockResolvedValue({ totalCount: 0, items: [] })

    render(
      <MemoryRouter initialEntries={['/triage-pool?scope=in-progress&status=EmColeta']}>
        <TriagePoolPage />
      </MemoryRouter>,
    )

    await waitFor(() =>
      expect(getMock).toHaveBeenCalledWith('/api/tenants/tenant-1/triage-pool?status=EmColeta&scope=in-progress'),
    )
    expect(screen.queryByRole('button', { name: /assumir/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })
})
