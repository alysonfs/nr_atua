/**
 * Test suite for TriagePoolTable (C1 — lista do pool).
 *
 * Cobre:
 * - Renderização dos cabeçalhos de coluna esperados.
 * - Renderização de uma linha por triagem retornada pela API.
 * - Aplicação do filtro de estado na query string.
 * - Estado vazio, de carregamento e de erro.
 * - Indicador de "nova atividade".
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { TriagePoolTable } from '../../components/TriagePoolTable'

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

const mockedItems = [
  {
    triagemId: 'triagem-1',
    workOrderProviderNo: 'BRWO260909869',
    customerName: 'Maria Souza',
    productModel: 'CRM43',
    productBrand: 'Consul',
    status: 'LiberadaAoPool',
    checklistItemsCompleted: 4,
    checklistItemsTotal: 5,
    enteredPoolAtUtc: '2026-10-20T10:00:00Z',
    hasNewActivity: true,
  },
  {
    triagemId: 'triagem-2',
    workOrderProviderNo: 'BRWO260909870',
    customerName: 'João Lima',
    productModel: 'W11A',
    productBrand: 'Brastemp',
    status: 'Expirada',
    checklistItemsCompleted: 2,
    checklistItemsTotal: 5,
    enteredPoolAtUtc: '2026-10-19T10:00:00Z',
    hasNewActivity: false,
  },
]

function renderTable(props: { tenantId: string | null; statusFilter?: string }) {
  return render(
    <MemoryRouter>
      <TriagePoolTable {...(props as never)} />
    </MemoryRouter>,
  )
}

describe('TriagePoolTable', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('renders the expected column headers', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 0, items: [] })

    renderTable({ tenantId: TENANT_ID })

    await waitFor(() => expect(screen.getByRole('columnheader', { name: /nº da os/i })).toBeInTheDocument())
    expect(screen.getByRole('columnheader', { name: /consumidor/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /produto\/modelo/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /^estado$/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /^checklist$/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /tempo no pool/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /tempo de triagem/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /custo de ia/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /nova atividade/i })).toBeInTheDocument()
  })

  it('renders one row per triagem returned by the API', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 2, items: mockedItems })

    renderTable({ tenantId: TENANT_ID })

    await waitFor(() => expect(screen.getByText('BRWO260909869')).toBeInTheDocument())

    const rows = screen.getAllByRole('row')
    // uma linha de cabeçalho + uma por triagem retornada
    expect(rows).toHaveLength(mockedItems.length + 1)
    expect(screen.getByText('Maria Souza')).toBeInTheDocument()
    expect(screen.getByText('4/5')).toBeInTheDocument()
    expect(screen.getByText('2/5')).toBeInTheDocument()
  })

  it('renders the checklist state label in Portuguese', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 2, items: mockedItems })

    renderTable({ tenantId: TENANT_ID })

    await waitFor(() => expect(screen.getByText('Liberada ao pool')).toBeInTheDocument())
    expect(screen.getByText('Expirada')).toBeInTheDocument()
  })

  it('shows the new activity indicator only for items with hasNewActivity', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 2, items: mockedItems })

    renderTable({ tenantId: TENANT_ID })

    await waitFor(() => expect(screen.getByText('BRWO260909869')).toBeInTheDocument())

    expect(screen.getAllByTitle('Nova mensagem do consumidor')).toHaveLength(1)
  })

  it('renders the triage duration for items with startedAtUtc/finishedAtUtc', async () => {
    const startedInProgress = new Date(Date.now() - 60 * 60_000).toISOString()

    getMock.mockResolvedValueOnce({
      totalCount: 2,
      items: [
        { ...mockedItems[0], startedAtUtc: startedInProgress, finishedAtUtc: null },
        { ...mockedItems[1], startedAtUtc: '2026-10-20T09:00:00Z', finishedAtUtc: '2026-10-20T09:12:00Z' },
      ],
    })

    renderTable({ tenantId: TENANT_ID })

    await waitFor(() => expect(screen.getByText('BRWO260909869')).toBeInTheDocument())

    expect(screen.getByText(/\(em andamento\)/)).toBeInTheDocument()
    expect(screen.getByText('Finalizada em 12 min')).toBeInTheDocument()
  })

  it('renders "—" for triage duration when startedAtUtc is missing', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 1, items: [mockedItems[0]] })

    renderTable({ tenantId: TENANT_ID })

    await waitFor(() => expect(screen.getByText('BRWO260909869')).toBeInTheDocument())

    const row = screen.getByText('BRWO260909869').closest('tr')
    expect(row).not.toBeNull()
    expect(row).toHaveTextContent('—')
  })

  it('renders the AI cost in USD when llmUsage is present, and "—" when absent', async () => {
    getMock.mockResolvedValueOnce({
      totalCount: 2,
      items: [
        { ...mockedItems[0], llmUsage: { inputTokens: 500, outputTokens: 100, estimatedCostUsd: 0.0123, calls: 2 } },
        mockedItems[1],
      ],
    })

    renderTable({ tenantId: TENANT_ID })

    await waitFor(() => expect(screen.getByText('BRWO260909869')).toBeInTheDocument())

    expect(screen.getByText(/US\$\s?0,0123/)).toBeInTheDocument()

    const secondRow = screen.getByText('BRWO260909870').closest('tr')
    expect(secondRow).not.toBeNull()
    expect(secondRow).toHaveTextContent('—')
  })

  it('applies the status filter in the request', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 0, items: [] })

    renderTable({ tenantId: TENANT_ID, statusFilter: 'Expirada' })

    await waitFor(() =>
      expect(getMock).toHaveBeenCalledWith(`/api/tenants/${TENANT_ID}/triage-pool?status=Expirada`),
    )
  })

  it('renders an empty state message when there are no items', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 0, items: [] })

    renderTable({ tenantId: TENANT_ID })

    await waitFor(() => expect(screen.getByText(/nenhuma triagem no pool/i)).toBeInTheDocument())
  })

  it('renders an error message when the request fails', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    renderTable({ tenantId: TENANT_ID })

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
  })
})
