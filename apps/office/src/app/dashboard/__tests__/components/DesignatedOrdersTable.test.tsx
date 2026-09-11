/**
 * Test suite for DesignatedOrdersTable dashboard section.
 *
 * Cobre:
 * - Renderização dos cabeçalhos de coluna esperados.
 * - Renderização de uma linha por OS mockada, com contagem correta.
 * - Renderização do heading da seção.
 * - Estado vazio (sem itens), garantindo que a tabela não quebra.
 */

import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { DesignatedOrdersTable } from '../../components/DesignatedOrdersTable'
import { useDesignatedServiceOrders } from '../../hooks/useDesignatedServiceOrders'

vi.mock('../../hooks/useDesignatedServiceOrders', async () => {
  const actual = await vi.importActual<typeof import('../../hooks/useDesignatedServiceOrders')>(
    '../../hooks/useDesignatedServiceOrders',
  )
  return {
    ...actual,
    useDesignatedServiceOrders: vi.fn(actual.useDesignatedServiceOrders),
  }
})

describe('DesignatedOrdersTable', () => {
  it('renders the section heading', () => {
    render(<DesignatedOrdersTable />)

    expect(screen.getByRole('heading', { name: /ordens de serviço designadas/i })).toBeInTheDocument()
  })

  it('renders the expected column headers', () => {
    render(<DesignatedOrdersTable />)

    expect(screen.getByRole('columnheader', { name: /^os$/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /provedor/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /^status$/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /criada em/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /atualizada em/i })).toBeInTheDocument()
  })

  it('renders one row per mocked designated service order', () => {
    render(<DesignatedOrdersTable />)

    const expectedOrders = useDesignatedServiceOrders()

    expectedOrders.forEach((order) => {
      expect(screen.getByText(order.id)).toBeInTheDocument()
      // um mesmo provedor pode ter mais de uma OS designada, então usamos
      // getAllByText para não quebrar quando o nome se repete.
      expect(screen.getAllByText(order.providerName).length).toBeGreaterThan(0)
    })

    const rows = screen.getAllByRole('row')
    // uma linha de cabeçalho + uma por OS mockada
    expect(rows).toHaveLength(expectedOrders.length + 1)
  })

  it('renders every row with the "Designado" status', () => {
    render(<DesignatedOrdersTable />)

    const expectedOrders = useDesignatedServiceOrders()
    const statusCells = screen.getAllByText('Designado')

    expect(statusCells).toHaveLength(expectedOrders.length)
  })

  it('renders an empty state message when there are no designated orders', () => {
    vi.mocked(useDesignatedServiceOrders).mockReturnValueOnce([])

    render(<DesignatedOrdersTable />)

    expect(screen.getByText(/nenhuma ordem de serviço designada/i)).toBeInTheDocument()
  })
})
