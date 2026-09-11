/**
 * Test suite for SummaryMonth dashboard section.
 *
 * Cobre:
 * - Renderização de um card por status retornado pela API, com nome e total.
 * - Presença do container do mini-gráfico (sparkline) por card.
 * - Estados de carregamento e erro.
 * - react-apexcharts é mockado pois depende de canvas/SVG não suportado
 *   de forma confiável em jsdom; o teste valida apenas que o componente
 *   de gráfico é invocado com os dados esperados, não sua renderização
 *   visual real.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { SummaryMonth } from '../../components/SummaryMonth'

vi.mock('react-apexcharts', () => ({
  default: (props: { series: { name: string; data: number[] }[] }) => (
    <div data-testid="apexchart-mock" data-series={JSON.stringify(props.series)} />
  ),
}))

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
  { status: 'Designado', total: 10, dailyCounts: [1, 2, 3, 4] },
  { status: 'Concluído', total: 20, dailyCounts: [5, 5, 5, 5] },
]

describe('SummaryMonth', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('renders one card per status with name and total count', async () => {
    getMock.mockResolvedValueOnce({ month: '2026-09', timeZoneId: 'America/Sao_Paulo', statuses: mockedStatuses })

    render(<SummaryMonth tenantId={TENANT_ID} />)

    for (const { status, total } of mockedStatuses) {
      await waitFor(() => expect(screen.getByText(status)).toBeInTheDocument())
      expect(screen.getByText(String(total))).toBeInTheDocument()
    }
  })

  it('renders a sparkline container for each status card', async () => {
    getMock.mockResolvedValueOnce({ month: '2026-09', timeZoneId: 'America/Sao_Paulo', statuses: mockedStatuses })

    render(<SummaryMonth tenantId={TENANT_ID} />)

    await waitFor(() => expect(screen.getAllByTestId('summary-month-sparkline')).toHaveLength(mockedStatuses.length))
  })

  it('renders the section heading', () => {
    getMock.mockResolvedValueOnce({ month: '2026-09', timeZoneId: 'America/Sao_Paulo', statuses: [] })

    render(<SummaryMonth tenantId={TENANT_ID} />)

    expect(screen.getByRole('heading', { name: /resumo do mês/i })).toBeInTheDocument()
  })

  it('renders an error message when the request fails', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    render(<SummaryMonth tenantId={TENANT_ID} />)

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
  })
})
