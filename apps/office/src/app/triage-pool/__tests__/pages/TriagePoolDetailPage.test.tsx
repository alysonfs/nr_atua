/**
 * Test suite for TriagePoolDetailPage (C2 + C3).
 *
 * Cobre:
 * - Estado de carregamento.
 * - Renderização do checklist, anexos, conversa e motivo do estado.
 * - Mensagem de "não encontrada" em 404.
 * - Assumir com sucesso: dispara refetch do detalhe.
 * - Assumir com 409: exibe aviso de quem já assumiu.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { TriagePoolDetailPage } from '../../pages/TriagePoolDetailPage'
import { useTriagePoolDetail } from '../../hooks/useTriagePoolDetail'
import { useAssumeTriagem } from '../../hooks/useAssumeTriagem'
import { useMyTenants } from '../../../office/hooks/useTenants'

vi.mock('../../hooks/useTriagePoolDetail', () => ({
  useTriagePoolDetail: vi.fn(),
}))

vi.mock('../../hooks/useAssumeTriagem', () => ({
  useAssumeTriagem: vi.fn(),
}))

vi.mock('../../../office/hooks/useTenants', () => ({
  useMyTenants: vi.fn(),
}))

const mockedUseTriagePoolDetail = vi.mocked(useTriagePoolDetail)
const mockedUseAssumeTriagem = vi.mocked(useAssumeTriagem)
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

    mockedUseAssumeTriagem.mockReturnValue({
      assume: vi.fn(),
      isSubmitting: false,
    })
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
    expect(screen.getByRole('button', { name: /assumir/i })).toBeInTheDocument()
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

  it('does not render the assume button when the triagem is already assumed', () => {
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

  it('assumes successfully and triggers a refetch of the detail', async () => {
    const user = userEvent.setup()
    const refetch = vi.fn()
    const assume = vi.fn().mockResolvedValue({
      status: 'success',
      response: {
        triagemId: 'triagem-1',
        status: 'AssumidaPorOperador',
        assumidaPorUserId: 'user-1',
        assumidaEmUtc: '2026-10-22T10:00:00Z',
      },
    })

    mockedUseTriagePoolDetail.mockReturnValue({
      detail: mockedDetail,
      isLoading: false,
      isError: false,
      notFound: false,
      refetch,
    })
    mockedUseAssumeTriagem.mockReturnValue({ assume, isSubmitting: false })

    renderPage()

    await user.click(screen.getByRole('button', { name: /assumir/i }))
    await user.click(screen.getByRole('button', { name: /^confirmar$/i }))

    await waitFor(() => expect(assume).toHaveBeenCalledWith('triagem-1'))
    await waitFor(() => expect(refetch).toHaveBeenCalled())
  })

  it('shows who already assumed the triagem on a 409 conflict', async () => {
    const user = userEvent.setup()
    const assume = vi.fn().mockResolvedValue({
      status: 'conflict',
      conflict: {
        error: 'triagem_already_assumed',
        status: 'AssumidaPorOperador',
        assumedByUserId: 'user-2',
        assumedAtUtc: '2026-10-22T09:00:00Z',
      },
    })

    mockedUseTriagePoolDetail.mockReturnValue({
      detail: mockedDetail,
      isLoading: false,
      isError: false,
      notFound: false,
      refetch: vi.fn(),
    })
    mockedUseAssumeTriagem.mockReturnValue({ assume, isSubmitting: false })

    renderPage()

    await user.click(screen.getByRole('button', { name: /assumir/i }))
    await user.click(screen.getByRole('button', { name: /^confirmar$/i }))

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/já assumida por user-2 em/i))
  })
})
