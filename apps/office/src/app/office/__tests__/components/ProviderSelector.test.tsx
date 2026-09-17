import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ProviderSelector } from '../../components/Integrations/ProviderSelector'

describe('ProviderSelector', () => {
  it('exibe o provedor iService disponível para seleção', () => {
    render(<ProviderSelector selectedProvider={null} onSelect={vi.fn()} />)

    expect(screen.getByText('iService')).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /iService/ })).toHaveAttribute('aria-checked', 'false')
  })

  it('chama onSelect ao clicar em um provedor', async () => {
    const onSelect = vi.fn()
    const user = userEvent.setup()
    render(<ProviderSelector selectedProvider={null} onSelect={onSelect} />)

    await user.click(screen.getByRole('radio', { name: /iService/ }))

    expect(onSelect).toHaveBeenCalledWith('iservice')
  })

  it('marca o provedor selecionado como checado', () => {
    render(<ProviderSelector selectedProvider="iservice" onSelect={vi.fn()} />)

    expect(screen.getByRole('radio', { name: /iService/ })).toHaveAttribute('aria-checked', 'true')
  })
})
