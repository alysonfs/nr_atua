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

  it('renders a "Ver triagem" link when triagemId is present', () => {
    mockedUseWorkOrderDetail.mockReturnValue({
      detail: { ...mockedDetail, triagemId: 'triagem-1' },
      isLoading: false,
      isError: false,
      notFound: false,
    })

    renderPage()

    const link = screen.getByRole('link', { name: /ver triagem/i })
    expect(link).toHaveAttribute('href', '/triage-pool/triagem-1')
  })

  it('does not render a "Ver triagem" link when triagemId is absent', () => {
    mockedUseWorkOrderDetail.mockReturnValue({
      detail: mockedDetail,
      isLoading: false,
      isError: false,
      notFound: false,
    })

    renderPage()

    expect(screen.queryByRole('link', { name: /ver triagem/i })).not.toBeInTheDocument()
  })

  it('does not render the "Triagem" card when triagemId is absent', () => {
    mockedUseWorkOrderDetail.mockReturnValue({
      detail: mockedDetail,
      isLoading: false,
      isError: false,
      notFound: false,
    })

    renderPage()

    expect(screen.queryByText('Triagem')).not.toBeInTheDocument()
  })

  it('renders the "Triagem" card with duration, AI cost, invoice and warranty badge', () => {
    mockedUseWorkOrderDetail.mockReturnValue({
      detail: {
        ...mockedDetail,
        triagemId: 'triagem-1',
        triagemStartedAtUtc: '2026-10-20T10:00:00Z',
        triagemFinishedAtUtc: '2026-10-20T10:12:00Z',
        llmUsage: { estimatedCostUsd: 0.0123, inputTokens: 500, outputTokens: 100, calls: 2 },
        notaFiscalNumero: '12345',
        notaFiscalSerie: '1',
        warrantyEstimate: {
          status: 'ProvavelmenteForaDaGarantia',
          baseDate: '2026-05-10T00:00:00Z',
          defaultMonths: 12,
        },
      },
      isLoading: false,
      isError: false,
      notFound: false,
    })

    renderPage()

    expect(screen.getByText('Triagem')).toBeInTheDocument()
    expect(screen.getByText('Finalizada em 12 min')).toBeInTheDocument()
    expect(screen.getByText(/US\$\s?0,0123/)).toBeInTheDocument()
    expect(screen.getByText(/12345/)).toBeInTheDocument()
    expect(screen.getByText(/provavelmente fora da garantia \(estimativa: 1 ano\)/i)).toBeInTheDocument()
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
