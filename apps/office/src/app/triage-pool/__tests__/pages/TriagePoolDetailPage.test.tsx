/**
 * Test suite for TriagePoolDetailPage (C2 — somente leitura).
 *
 * Cobre:
 * - Estado de carregamento.
 * - Renderização do checklist, anexos, conversa e motivo do estado.
 * - Mensagem de "não encontrada" em 404.
 * - Triagem pré-pool (em triagem) exibida com o mesmo detalhe.
 * - Nenhuma ação de atendimento (sem "Assumir", sem campo de mensagem).
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { TriagePoolDetailPage } from '../../pages/TriagePoolDetailPage'
import { useTriagePoolDetail } from '../../hooks/useTriagePoolDetail'
import { useMyTenants } from '../../../office/hooks/useTenants'

vi.mock('../../hooks/useTriagePoolDetail', () => ({
  useTriagePoolDetail: vi.fn(),
}))

vi.mock('../../../office/hooks/useTenants', () => ({
  useMyTenants: vi.fn(),
}))

const mockedUseTriagePoolDetail = vi.mocked(useTriagePoolDetail)
const mockedUseMyTenants = vi.mocked(useMyTenants)

const mockedDetail = {
  triagemId: 'triagem-1',
  status: 'LiberadaAoPool',
  stateReason: 'Expirada em 48h sem checklist mínimo.',
  enteredPoolAtUtc: '2026-10-20T10:00:00Z',
  assumidaPorUserId: null,
  assumidaEmUtc: null,
  workOrder: {
    workOrderId: 'wo-1',
    workOrderProviderNo: 'BRWO260909869',
    customerName: 'Maria Souza',
    customerCpf: '123.456.789-00',
    contactPhone: '84999999999',
    contactEmail: 'maria@example.com',
    address: 'Rua das Flores, 123',
    zipCode: '59000-000',
    cityName: 'Natal',
    stateName: 'RN',
    productBrand: 'Consul',
    productModel: 'CRM43',
    symptom: 'Não gela',
  },
  checklist: [
    { item: 'DescricaoDefeito', required: true, completed: true, value: 'Não gela' },
    { item: 'Endereco', required: true, completed: false, value: null },
  ],
  attachments: [],
  conversation: [{ atUtc: '2026-10-20T09:00:00Z', sender: 'consumidor', kind: 'text', text: 'Olá', attachmentMediaId: null }],
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/triage-pool/triagem-1']}>
      <Routes>
        <Route path="/triage-pool/:triagemId" element={<TriagePoolDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('TriagePoolDetailPage', () => {
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
    mockedUseTriagePoolDetail.mockReturnValue({
      detail: null,
      isLoading: true,
      isError: false,
      notFound: false,
      refetch: vi.fn(),
    })

    renderPage()

    expect(screen.getByText(/carregando detalhes da triagem/i)).toBeInTheDocument()
  })

  it('renders checklist, attachments-empty, conversation and state reason', () => {
    mockedUseTriagePoolDetail.mockReturnValue({
      detail: mockedDetail,
      isLoading: false,
      isError: false,
      notFound: false,
      refetch: vi.fn(),
    })

    renderPage()

    expect(screen.getByText(/expirada em 48h sem checklist mínimo/i)).toBeInTheDocument()
    expect(screen.getByText('Descrição do defeito')).toBeInTheDocument()
    expect(screen.getByText('Endereço completo')).toBeInTheDocument()
    expect(screen.getByText(/nenhum anexo recebido/i)).toBeInTheDocument()
    expect(screen.getByText('Olá')).toBeInTheDocument()
    expect(screen.getByText(/somente leitura/i)).toBeInTheDocument()
  })

  it('renders a "Ver OS" link pointing to the related work order', () => {
    mockedUseTriagePoolDetail.mockReturnValue({
      detail: mockedDetail,
      isLoading: false,
      isError: false,
      notFound: false,
      refetch: vi.fn(),
    })

    renderPage()

    const link = screen.getByRole('link', { name: /ver os/i })
    expect(link).toHaveAttribute('href', '/work-orders/wo-1')
  })

  it('renders the triage duration, showing "in progress" without finishedAtUtc', () => {
    const startedInProgress = new Date(Date.now() - 2 * 60 * 60_000).toISOString()

    mockedUseTriagePoolDetail.mockReturnValue({
      detail: { ...mockedDetail, startedAtUtc: startedInProgress, finishedAtUtc: null },
      isLoading: false,
      isError: false,
      notFound: false,
      refetch: vi.fn(),
    })

    renderPage()

    expect(screen.getByText(/\(em andamento\)/)).toBeInTheDocument()
  })

  it('renders the triage duration as "finalizada em" when finishedAtUtc is set', () => {
    mockedUseTriagePoolDetail.mockReturnValue({
      detail: {
        ...mockedDetail,
        startedAtUtc: '2026-10-20T10:00:00Z',
        finishedAtUtc: '2026-10-20T10:12:00Z',
      },
      isLoading: false,
      isError: false,
      notFound: false,
      refetch: vi.fn(),
    })

    renderPage()

    expect(screen.getByText(/finalizada em 12 min/i)).toBeInTheDocument()
  })

  it('renders "—" for triage duration when startedAtUtc is missing', () => {
    mockedUseTriagePoolDetail.mockReturnValue({
      detail: mockedDetail,
      isLoading: false,
      isError: false,
      notFound: false,
      refetch: vi.fn(),
    })

    renderPage()

    expect(screen.getByText(`${'Tempo de triagem'}: —`)).toBeInTheDocument()
  })

  it('renders no attendance actions (no assume button, no message input)', () => {
    mockedUseTriagePoolDetail.mockReturnValue({
      detail: mockedDetail,
      isLoading: false,
      isError: false,
      notFound: false,
      refetch: vi.fn(),
    })

    renderPage()

    expect(screen.queryByRole('button', { name: /assumir/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('renders a pre-pool (in-progress) triagem with the same read-only detail', () => {
    mockedUseTriagePoolDetail.mockReturnValue({
      detail: { ...mockedDetail, status: 'EmColeta', stateReason: 'Coleta em andamento com o consumidor.' },
      isLoading: false,
      isError: false,
      notFound: false,
      refetch: vi.fn(),
    })

    renderPage()

    expect(screen.getByText('Em coleta')).toBeInTheDocument()
    expect(screen.getByText(/coleta em andamento com o consumidor/i)).toBeInTheDocument()
    expect(screen.getByText('Descrição do defeito')).toBeInTheDocument()
    expect(screen.getByText('Olá')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /assumir/i })).not.toBeInTheDocument()
  })

  it('shows a friendly not-found message on 404', () => {
    mockedUseTriagePoolDetail.mockReturnValue({
      detail: null,
      isLoading: false,
      isError: false,
      notFound: true,
      refetch: vi.fn(),
    })

    renderPage()

    expect(screen.getByRole('alert')).toHaveTextContent(/triagem não encontrada ou sem acesso/i)
  })

  it('shows who assumed the triagem, read-only, when it is already assumed', () => {
    mockedUseTriagePoolDetail.mockReturnValue({
      detail: { ...mockedDetail, status: 'AssumidaPorOperador', assumidaPorUserId: 'user-1', assumidaEmUtc: '2026-10-22T10:00:00Z' },
      isLoading: false,
      isError: false,
      notFound: false,
      refetch: vi.fn(),
    })

    renderPage()

    expect(screen.queryByRole('button', { name: /assumir/i })).not.toBeInTheDocument()
    expect(screen.getByText(/assumida por user-1 em/i)).toBeInTheDocument()
  })
})
