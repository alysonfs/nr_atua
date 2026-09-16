import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { TenantSelector } from '../../components/Onboarding/TenantSelector'

describe('TenantSelector', () => {
  it('não renderiza nada quando não há tenants', () => {
    const { container } = render(<TenantSelector tenants={[]} onSelect={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('exige seleção explícita entre múltiplos tenants antes de habilitar o botão', async () => {
    const onSelect = vi.fn()
    const user = userEvent.setup()
    render(
      <TenantSelector
        tenants={[
          { tenantId: 't1', name: 'Empresa A', role: 'OWNER' },
          { tenantId: 't2', name: 'Empresa B', role: 'OWNER' },
        ]}
        onSelect={onSelect}
      />,
    )

    const button = screen.getByRole('button', { name: 'Acessar' })
    expect(button).toBeDisabled()

    await user.selectOptions(screen.getByLabelText('Empresa'), 't2')
    expect(button).toBeEnabled()

    await user.click(button)
    expect(onSelect).toHaveBeenCalledWith('t2')
  })
})
