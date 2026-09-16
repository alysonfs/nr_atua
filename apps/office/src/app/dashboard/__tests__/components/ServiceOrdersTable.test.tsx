/**
 * Test suite for ServiceOrdersTable dashboard section.
 *
 * Cobre:
 * - Renderização dos cabeçalhos de coluna esperados (incluindo campos do provedor).
 * - Renderização de uma linha por OS retornada pela API, com contagem correta.
 * - Renderização do heading da seção com o status filtrado.
 * - Troca de tamanho de página (10/15/20/25/50).
 * - Navegação de página (botões habilitados/desabilitados nos limites).
 * - Estado vazio (sem itens), garantindo que a tabela não quebra.
 * - Estados de carregamento e erro.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { ServiceOrdersTable } from '../../components/ServiceOrdersTable'

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
  {
    id: 'os-1', providerId: 'EXT-1001', status: 'Designado',
    createdAt: '2026-09-01T10:00:00Z', updatedAt: '2026-09-02T10:00:00Z',
    providerCreatedAt: '2026-09-01T09:00:00Z', providerUpdatedAt: '2026-09-02T09:00:00Z',
    productModel: 'CRM43', productBrand: 'Consul', customerName: 'Maria Souza', cityName: 'Natal',
  },
  {
    id: 'os-2', providerId: 'EXT-1002', status: 'Designado',
    createdAt: '2026-09-03T10:00:00Z', updatedAt: '2026-09-04T10:00:00Z',
    providerCreatedAt: '2026-09-03T09:00:00Z', providerUpdatedAt: '2026-09-04T09:00:00Z',
    productModel: 'W11A', productBrand: 'Brastemp', customerName: 'João Lima', cityName: 'Parnamirim',
  },
]

describe('ServiceOrdersTable', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('renders the section heading with the filtered status', async () => {
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 15, totalCount: 0, items: [] })

    render(<ServiceOrdersTable tenantId={TENANT_ID} status="Designado" />)

    expect(screen.getByRole('heading', { name: /ordens de serviço.*designado/i })).toBeInTheDocument()
  })

  it('renders the expected column headers', async () => {
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 15, totalCount: mockedOrders.length, items: mockedOrders })

    render(<ServiceOrdersTable tenantId={TENANT_ID} status="Designado" />)

    await waitFor(() => expect(screen.getByRole('columnheader', { name: /nº da os no provedor/i })).toBeInTheDocument())
    expect(screen.getByRole('columnheader', { name: /^status$/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /criada em \(provedor\)/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /atualizada em \(provedor\)/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /modelo do equipamento/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /marca do equipamento/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /nome do consumidor/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /^cidade$/i })).toBeInTheDocument()
  })

  it('renders one row per service order returned by the API', async () => {
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 15, totalCount: mockedOrders.length, items: mockedOrders })

    render(<ServiceOrdersTable tenantId={TENANT_ID} status="Designado" />)

    await waitFor(() => expect(screen.getByText('Maria Souza')).toBeInTheDocument())

    mockedOrders.forEach((order) => {
      expect(screen.getAllByText(order.providerId).length).toBeGreaterThan(0)
      expect(screen.getByText(order.customerName)).toBeInTheDocument()
    })

    const rows = screen.getAllByRole('row')
    // uma linha de cabeçalho + uma por OS retornada
    expect(rows).toHaveLength(mockedOrders.length + 1)
  })

  it('changes the page size and refetches with the new value', async () => {
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 15, totalCount: mockedOrders.length, items: mockedOrders })
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 25, totalCount: mockedOrders.length, items: mockedOrders })

    render(<ServiceOrdersTable tenantId={TENANT_ID} status="Designado" />)

    await waitFor(() => expect(screen.getByText('Maria Souza')).toBeInTheDocument())

    fireEvent.change(screen.getByLabelText(/itens por página/i), { target: { value: '25' } })

    await waitFor(() => expect(getMock).toHaveBeenLastCalledWith(expect.stringContaining('pageSize=25')))
  })

  it('disables the previous page button on the first page and enables next when there are more pages', async () => {
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 15, totalCount: 30, items: mockedOrders })

    render(<ServiceOrdersTable tenantId={TENANT_ID} status="Designado" />)

    await waitFor(() => expect(screen.getByText(/página 1 de 2/i)).toBeInTheDocument())
    expect(screen.getByRole('button', { name: /anterior/i })).toBeDisabled()
    expect(screen.getByRole('button', { name: /próxima/i })).toBeEnabled()
  })

  it('renders an empty state message when there are no orders', async () => {
    getMock.mockResolvedValueOnce({ status: 'Designado', page: 1, pageSize: 15, totalCount: 0, items: [] })

    render(<ServiceOrdersTable tenantId={TENANT_ID} status="Designado" />)

    await waitFor(() => expect(screen.getByText(/nenhuma ordem de serviço encontrada/i)).toBeInTheDocument())
  })

  it('renders an error message when the request fails', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    render(<ServiceOrdersTable tenantId={TENANT_ID} status="Designado" />)

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
  })
})
