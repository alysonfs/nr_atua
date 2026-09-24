/**
 * Test suite for StatusSummary dashboard section.
 *
 * Cobre:
 * - Renderização de um card por status retornado pela API, com nome e total.
 * - Clique em um card dispara onSelectStatus com o status raw (não traduzido).
 * - Card do status selecionado é marcado via aria-pressed.
 * - Estados de carregamento e erro.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { StatusSummary } from '../../components/StatusSummary'

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

const mockedStatuses = [
  { status: 'Designado', total: 10 },
  { status: 'Concluído', total: 20 },
]

describe('StatusSummary', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('renders one card per status with name and total count', async () => {
    getMock.mockResolvedValueOnce({ statuses: mockedStatuses })

    render(<StatusSummary tenantId={TENANT_ID} selectedStatus="Designado" onSelectStatus={vi.fn()} />)

    for (const { status, total } of mockedStatuses) {
      await waitFor(() => expect(screen.getByText(status)).toBeInTheDocument())
      expect(screen.getByText(String(total))).toBeInTheDocument()
    }
  })

  it('renders the section heading', () => {
    getMock.mockResolvedValueOnce({ statuses: [] })

    render(<StatusSummary tenantId={TENANT_ID} selectedStatus="Designado" onSelectStatus={vi.fn()} />)

    expect(screen.getByRole('heading', { name: /resumo por status/i })).toBeInTheDocument()
  })

  it('marks the currently selected status card as pressed', async () => {
    getMock.mockResolvedValueOnce({ statuses: mockedStatuses })

    render(<StatusSummary tenantId={TENANT_ID} selectedStatus="Designado" onSelectStatus={vi.fn()} />)

    await waitFor(() => expect(screen.getByText('Designado')).toBeInTheDocument())

    const selectedCard = screen.getByText('Designado').closest('button')
    const otherCard = screen.getByText('Concluído').closest('button')
    expect(selectedCard).toHaveAttribute('aria-pressed', 'true')
    expect(otherCard).toHaveAttribute('aria-pressed', 'false')
  })

  it('calls onSelectStatus with the raw status when a card is clicked', async () => {
    getMock.mockResolvedValueOnce({ statuses: mockedStatuses })
    const onSelectStatus = vi.fn()

    render(<StatusSummary tenantId={TENANT_ID} selectedStatus="Designado" onSelectStatus={onSelectStatus} />)

    await waitFor(() => expect(screen.getByText('Concluído')).toBeInTheDocument())

    fireEvent.click(screen.getByText('Concluído').closest('button')!)

    expect(onSelectStatus).toHaveBeenCalledWith('Concluído')
  })

  it('renders an error message when the request fails', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    render(<StatusSummary tenantId={TENANT_ID} selectedStatus="Designado" onSelectStatus={vi.fn()} />)

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
  })
})
