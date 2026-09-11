/**
 * Test suite for SummaryMonth dashboard section.
 *
 * Cobre:
 * - Renderização de um card por status, com nome e contagem total.
 * - Presença do container do mini-gráfico (sparkline) por card.
 * - react-apexcharts é mockado pois depende de canvas/SVG não suportado
 *   de forma confiável em jsdom; o teste valida apenas que o componente
 *   de gráfico é invocado com os dados esperados, não sua renderização
 *   visual real.
 */

import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SummaryMonth } from '../../components/SummaryMonth'
import { useServiceOrderMonthSummary } from '../../hooks/useServiceOrderMonthSummary'

vi.mock('react-apexcharts', () => ({
  default: (props: { series: { name: string; data: number[] }[] }) => (
    <div data-testid="apexchart-mock" data-series={JSON.stringify(props.series)} />
  ),
}))

describe('SummaryMonth', () => {
  it('renders one card per status with name and total count', () => {
    render(<SummaryMonth />)

    const expectedSummary = useServiceOrderMonthSummary()

    expectedSummary.forEach(({ status, total }) => {
      expect(screen.getByText(status)).toBeInTheDocument()
      expect(screen.getByText(String(total))).toBeInTheDocument()
    })
  })

  it('renders a sparkline container for each status card', () => {
    render(<SummaryMonth />)

    const expectedSummary = useServiceOrderMonthSummary()
    const sparklines = screen.getAllByTestId('summary-month-sparkline')

    expect(sparklines).toHaveLength(expectedSummary.length)
  })

  it('renders the section heading', () => {
    render(<SummaryMonth />)

    expect(screen.getByRole('heading', { name: /resumo do mês/i })).toBeInTheDocument()
  })
})
