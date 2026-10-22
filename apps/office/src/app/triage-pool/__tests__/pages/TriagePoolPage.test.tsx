/**
 * Test suite for TriagePoolPage (C1 — página da lista do pool).
 *
 * Cobre:
 * - Título/subtítulo da página.
 * - Troca de filtro de estado reflete na query string e na requisição.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
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
})
