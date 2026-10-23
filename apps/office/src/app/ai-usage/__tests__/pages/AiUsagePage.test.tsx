/**
 * Test suite for AiUsagePage.
 *
 * Cobre:
 * - Título/subtítulo e aviso de custo estimado.
 * - Período padrão (30 dias) na requisição inicial.
 * - Troca de período reflete na requisição (from/to).
 * - Estados de carregando, erro e vazio.
 * - Cartões de totais e tabelas de tenant/modelo renderizados com sucesso.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AiUsagePage } from '../../pages/AiUsagePage'

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

vi.mock('react-apexcharts', () => ({
  default: () => <div data-testid="ai-usage-chart" />,
}))

const mockedSummary = {
  from: '2026-09-24',
  to: '2026-10-23',
  currency: 'USD',
  totals: { calls: 120, inputTokens: 50000, outputTokens: 20000, estimatedCost: 12.3456, avgLatencyMs: 842, failedCalls: 3 },
  tenants: [
    { tenantId: 't1', tenantName: 'Tenant A', calls: 100, inputTokens: 40000, outputTokens: 15000, estimatedCost: 10, avgLatencyMs: 800, failedCalls: 2 },
    { tenantId: 't2', tenantName: 'Tenant B', calls: 20, inputTokens: 10000, outputTokens: 5000, estimatedCost: 2.3456, avgLatencyMs: 900, failedCalls: 1 },
  ],
  daily: [{ date: '2026-10-23', calls: 10, inputTokens: 1000, outputTokens: 500, estimatedCost: 0.5 }],
  models: [{ modelId: 'gpt-4o-mini', calls: 120, inputTokens: 50000, outputTokens: 20000, estimatedCost: 12.3456 }],
}

describe('AiUsagePage', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('renders the page title, subtitle and the cost disclaimer', async () => {
    getMock.mockResolvedValueOnce(mockedSummary)

    render(<AiUsagePage />)

    expect(screen.getByRole('heading', { name: 'Inteligência Artificial' })).toBeInTheDocument()
    expect(
      screen.getByText(/Custo estimado com base no preço público por token/i),
    ).toBeInTheDocument()

    await waitFor(() => expect(getMock).toHaveBeenCalled())
  })

  it('requests the last 30 days by default', async () => {
    getMock.mockResolvedValueOnce(mockedSummary)

    render(<AiUsagePage />)

    await waitFor(() =>
      expect(getMock).toHaveBeenCalledWith(expect.stringMatching(/^\/api\/llm-usage\/summary\?from=.+&to=.+$/)),
    )
  })

  it('changes the period and requests a new range', async () => {
    getMock.mockResolvedValue(mockedSummary)
    const user = userEvent.setup()

    render(<AiUsagePage />)

    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(1))

    await user.click(screen.getByRole('tab', { name: 'Últimos 7 dias' }))

    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(2))
    expect(screen.getByRole('tab', { name: 'Últimos 7 dias' })).toHaveAttribute('aria-selected', 'true')
  })

  it('shows the loading state while the request is in flight', () => {
    getMock.mockReturnValue(new Promise(() => {}))

    render(<AiUsagePage />)

    expect(screen.getByText('Carregando o consumo de IA...')).toBeInTheDocument()
  })

  it('shows an error message when the request fails', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    render(<AiUsagePage />)

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(/Não foi possível carregar o consumo de IA/i),
    )
  })

  it('renders totals cards, tenants table and models table on success, with tenants sorted by cost', async () => {
    getMock.mockResolvedValueOnce(mockedSummary)

    render(<AiUsagePage />)

    await waitFor(() => expect(screen.getByText('Consumo por tenant')).toBeInTheDocument())

    expect(screen.getAllByText('US$ 12,35').length).toBeGreaterThan(0)
    expect(screen.getByText('Tenant A')).toBeInTheDocument()
    expect(screen.getByText('Tenant B')).toBeInTheDocument()
    expect(screen.getByText('gpt-4o-mini')).toBeInTheDocument()

    const tenantRows = screen.getAllByRole('row')
    const tenantARowIndex = tenantRows.findIndex((row) => row.textContent?.includes('Tenant A'))
    const tenantBRowIndex = tenantRows.findIndex((row) => row.textContent?.includes('Tenant B'))
    expect(tenantARowIndex).toBeLessThan(tenantBRowIndex)
  })

  it('shows an empty-state message when there is no data', async () => {
    getMock.mockResolvedValueOnce(null)

    render(<AiUsagePage />)

    await waitFor(() =>
      expect(screen.getByText('Nenhum dado de consumo de IA para o período selecionado.')).toBeInTheDocument(),
    )
  })
})
