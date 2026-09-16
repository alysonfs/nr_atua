/**
 * Test suite for DesignatedOrdersTable dashboard section.
 *
 * Cobre:
 * - Renderização dos cabeçalhos de coluna esperados.
 * - Renderização de uma linha por OS retornada pela API, com contagem correta.
 * - Renderização do heading da seção.
 * - Estado vazio (sem itens), garantindo que a tabela não quebra.
 * - Estados de carregamento e erro.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { DesignatedOrdersTable } from '../../components/DesignatedOrdersTable'

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

const TENANT_ID = 'tenant-1'

const mockedOrders = [
  { id: 'os-1', providerId: 'EXT-1001', status: 'Designado', createdAt: '2026-09-01T10:00:00Z', updatedAt: '2026-09-02T10:00:00Z' },
  { id: 'os-2', providerId: 'EXT-1002', status: 'Designado', createdAt: '2026-09-03T10:00:00Z', updatedAt: '2026-09-04T10:00:00Z' },
]

describe('DesignatedOrdersTable', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('renders the section heading', async () => {
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 100, totalCount: 0, items: [] })

    render(<DesignatedOrdersTable tenantId={TENANT_ID} />)

    expect(screen.getByRole('heading', { name: /ordens de serviço designadas/i })).toBeInTheDocument()
  })

  it('renders the expected column headers', async () => {
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 100, totalCount: mockedOrders.length, items: mockedOrders })

    render(<DesignatedOrdersTable tenantId={TENANT_ID} />)

    await waitFor(() => expect(screen.getByRole('columnheader', { name: /^os$/i })).toBeInTheDocument())
    expect(screen.getByRole('columnheader', { name: /nº da os no provedor/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /^status$/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /criada em/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /atualizada em/i })).toBeInTheDocument()
  })

  it('renders one row per designated service order returned by the API', async () => {
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 100, totalCount: mockedOrders.length, items: mockedOrders })

    render(<DesignatedOrdersTable tenantId={TENANT_ID} />)

    await waitFor(() => expect(screen.getByText('os-1')).toBeInTheDocument())

    mockedOrders.forEach((order) => {
      expect(screen.getByText(order.id)).toBeInTheDocument()
      expect(screen.getAllByText(order.providerId).length).toBeGreaterThan(0)
    })

    const rows = screen.getAllByRole('row')
    // uma linha de cabeçalho + uma por OS retornada
    expect(rows).toHaveLength(mockedOrders.length + 1)
  })

  it('renders every row with the "Designado" status', async () => {
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 100, totalCount: mockedOrders.length, items: mockedOrders })

    render(<DesignatedOrdersTable tenantId={TENANT_ID} />)

    await waitFor(() => expect(screen.getAllByText('Designado')).toHaveLength(mockedOrders.length))
  })

  it('renders an empty state message when there are no designated orders', async () => {
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 100, totalCount: 0, items: [] })

    render(<DesignatedOrdersTable tenantId={TENANT_ID} />)

    await waitFor(() => expect(screen.getByText(/nenhuma ordem de serviço designada/i)).toBeInTheDocument())
  })

  it('renders an error message when the request fails', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    render(<DesignatedOrdersTable tenantId={TENANT_ID} />)

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
  })
})
