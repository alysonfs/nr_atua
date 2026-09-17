import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ProviderIntegrationSection } from '../../components/Integrations/ProviderIntegrationSection'

vi.mock('../../hooks/useIServiceIntegration', () => ({
  useIServiceCredentials: () => ({
    status: null,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
  useSetIServiceCredentials: () => ({ setCredentials: vi.fn(), isSubmitting: false }),
  useValidateIServiceCredentials: () => ({ validate: vi.fn(), isValidating: false }),
}))

vi.mock('../../hooks/useCollectorActivation', () => ({
  useCollectorActivation: () => ({ status: null, isLoading: false, isError: false, refetch: vi.fn() }),
  useActivateCollector: () => ({ activate: vi.fn(), isActivating: false }),
  useDeactivateCollector: () => ({ deactivate: vi.fn(), isDeactivating: false }),
}))

vi.mock('../../hooks/useRecurrentCollectionInterval', () => ({
  useRecurrentCollectionInterval: () => ({
    interval: { recurrentCollectionIntervalMinutes: 15 },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
  useUpdateRecurrentCollectionInterval: () => ({ update: vi.fn(), isSaving: false }),
}))

describe('ProviderIntegrationSection', () => {
  it('não exibe o formulário de credenciais antes de escolher um provedor', () => {
    render(<ProviderIntegrationSection tenantId="tenant-1" integrationId="integration-1" />)

    expect(screen.queryByText('Credenciais do iService')).not.toBeInTheDocument()
  })

  it('exibe o formulário de credenciais do iService após a escolha do provedor', async () => {
    const user = userEvent.setup()
    render(<ProviderIntegrationSection tenantId="tenant-1" integrationId="integration-1" />)

    await user.click(screen.getByRole('radio', { name: /iService/ }))

    expect(screen.getByText('Credenciais do iService')).toBeInTheDocument()
  })

  it('recolhe a área ao clicar novamente no provedor já selecionado', async () => {
    const user = userEvent.setup()
    render(<ProviderIntegrationSection tenantId="tenant-1" integrationId="integration-1" />)

    const providerButton = screen.getByRole('radio', { name: /iService/ })
    await user.click(providerButton)
    expect(screen.getByText('Credenciais do iService')).toBeInTheDocument()

    await user.click(providerButton)
    expect(screen.queryByText('Credenciais do iService')).not.toBeInTheDocument()
    expect(providerButton).toHaveAttribute('aria-checked', 'false')
  })
})
