/**
 * Test suite for WorkOrderDetailPage.
 *
 * Cobre:
 * - Estado de carregamento.
 * - Renderização dos campos normalizados quando os dados chegam.
 * - Mensagem de "não encontrado" em 404, com link para voltar ao Dashboard.
 * - Renderização do histórico de status em ordem cronológica.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { WorkOrderDetailPage } from '../../pages/WorkOrderDetailPage'
import { useWorkOrderDetail } from '../../hooks/useWorkOrderDetail'
import { useMyTenants } from '../../../office/hooks/useTenants'

vi.mock('../../hooks/useWorkOrderDetail', () => ({
  useWorkOrderDetail: vi.fn(),
}))

vi.mock('../../../office/hooks/useTenants', () => ({
  useMyTenants: vi.fn(),
}))

const mockedUseWorkOrderDetail = vi.mocked(useWorkOrderDetail)
const mockedUseMyTenants = vi.mocked(useMyTenants)

const mockedDetail = {
  id: 'os-1',
  workOrderProviderId: 'EXT-1001',
  workOrderProviderNo: 'BRWO260909869',
  status: 'Designado',
  createdAt: '2026-09-01T10:00:00Z',
  updatedAt: '2026-09-02T10:00:00Z',
  providerCreatedAt: '2026-09-01T09:00:00Z',
  providerUpdatedAt: '2026-09-02T09:00:00Z',
  serviceRequestId: 'SR-1',
  amount: 150.5,
  customerType: 'Pessoa Física',
  customerName: 'Maria Souza',
  customerCpf: '123.456.789-00',
  contactEmail: 'maria@example.com',
  contactPhone: '84999999999',
  contactName: 'Maria Souza',
  address: 'Rua das Flores, 123',
  zipCode: '59000-000',
  countryName: 'Brasil',
  stateName: 'RN',
  cityName: 'Natal',
  productBrand: 'Consul',
  pdCode: 'PD-1',
  categoryId: 'cat-1',
  productCategoryCode: 'REFR',
  productCode: 'PC-1',
  productModel: 'CRM43',
  productStatus: 'Em análise',
  symptom: 'Não gela',
  history: [
    { status: 'Novo', createdAt: '2026-09-01T10:00:00Z' },
    { status: 'Designado', createdAt: '2026-09-02T10:00:00Z' },
  ],
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/work-orders/os-1']}>
      <Routes>
        <Route path="/work-orders/:workOrderId" element={<WorkOrderDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('WorkOrderDetailPage', () => {
  beforeEach(() => {
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

  it('renders a loading state while data is being fetched', () => {
    mockedUseWorkOrderDetail.mockReturnValue({
      detail: null,
      isLoading: true,
      isError: false,
      notFound: false,
    })

    renderPage()

    expect(screen.getByText(/carregando detalhes da ordem de serviço/i)).toBeInTheDocument()
  })

  it('renders the normalized fields when data arrives', () => {
    mockedUseWorkOrderDetail.mockReturnValue({
      detail: mockedDetail,
      isLoading: false,
      isError: false,
      notFound: false,
    })

    renderPage()

    expect(screen.getByText('EXT-1001')).toBeInTheDocument()
    expect(screen.getAllByText('Maria Souza').length).toBeGreaterThan(0)
    expect(screen.getByText('123.456.789-00')).toBeInTheDocument()
    expect(screen.getByText('Rua das Flores, 123')).toBeInTheDocument()
    expect(screen.getByText('Consul')).toBeInTheDocument()
    expect(screen.getByText('Não gela')).toBeInTheDocument()
  })

  it('shows a friendly not-found message on 404', () => {
    mockedUseWorkOrderDetail.mockReturnValue({
      detail: null,
      isLoading: false,
      isError: false,
      notFound: true,
    })

    renderPage()

    expect(screen.getByRole('alert')).toHaveTextContent(/não foi possível encontrar esta ordem de serviço/i)
  })

  it('renders the status history in chronological order', () => {
    mockedUseWorkOrderDetail.mockReturnValue({
      detail: mockedDetail,
      isLoading: false,
      isError: false,
      notFound: false,
    })

    renderPage()

    const items = screen.getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('Novo')
    expect(items[1]).toHaveTextContent('Designado')
  })
})
