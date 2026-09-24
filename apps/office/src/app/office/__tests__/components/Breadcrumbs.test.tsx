/**
 * Test suite for Breadcrumbs
 *
 * Verifica que a trilha de navegação:
 * - Não é exibida na raiz (/home)
 * - É exibida em rotas internas, com o link para o item anterior e o
 *   rótulo atual em destaque (sem link)
 */

import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { Breadcrumbs } from '../../components/Layout/Breadcrumbs'

function renderBreadcrumbs(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/home" element={<Breadcrumbs />} />
        <Route path="/settings" element={<Breadcrumbs />} />
        <Route path="/work-orders/:workOrderId" element={<Breadcrumbs />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('Breadcrumbs', () => {
  it('renders nothing on the dashboard root', () => {
    const { container } = renderBreadcrumbs('/home')

    expect(container).toBeEmptyDOMElement()
  })

  it('renders a trail with a link to the dashboard on the settings route', () => {
    renderBreadcrumbs('/settings')

    expect(screen.getByRole('link', { name: /dashboard/i })).toHaveAttribute('href', '/home')
    expect(screen.getByText('Configurações')).toBeInTheDocument()
  })

  it('renders a trail with a link to the dashboard on the work order detail route', () => {
    renderBreadcrumbs('/work-orders/abc-123')

    expect(screen.getByRole('link', { name: /dashboard/i })).toHaveAttribute('href', '/home')
    expect(screen.getByText('Detalhes da OS')).toBeInTheDocument()
  })
})
