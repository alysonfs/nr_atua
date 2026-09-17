import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { CollectorActivationPanel } from '../../components/Integrations/CollectorActivationPanel'
import type { CollectorActivationView } from '../../../../shared/types/integration'

const activateMock = vi.fn()
const refetchMock = vi.fn()

let statusMock: {
  status: CollectorActivationView | null
  isLoading: boolean
  isError: boolean
}

vi.mock('../../hooks/useCollectorActivation', () => ({
  useCollectorActivation: () => ({
    ...statusMock,
    refetch: refetchMock,
  }),
  useActivateCollector: () => ({
    activate: activateMock,
    isActivating: false,
  }),
}))

describe('CollectorActivationPanel', () => {
  it('exibe estado de carregamento', () => {
    statusMock = { status: null, isLoading: true, isError: false }
    render(<CollectorActivationPanel tenantId="tenant-1" integrationId="integration-1" />)

    expect(screen.getByText('Carregando status do Agente Coletor...')).toBeInTheDocument()
  })

  it('exibe erro quando a leitura do status falha', () => {
    statusMock = { status: null, isLoading: false, isError: true }
    render(<CollectorActivationPanel tenantId="tenant-1" integrationId="integration-1" />)

    expect(
      screen.getByText('Não foi possível carregar o status do Agente Coletor. Tente novamente mais tarde.'),
    ).toBeInTheDocument()
  })

  it('exibe o botão habilitado quando o plano é elegível', () => {
    statusMock = {
      status: {
        status: 'Inactive',
        canActivate: true,
        activationBlockReason: 'None',
        credentialValidationStatus: 'Succeeded',
        activatedAtUtc: null,
        deactivatedAtUtc: null,
        lastImmediateCommand: null,
      },
      isLoading: false,
      isError: false,
    }
    render(<CollectorActivationPanel tenantId="tenant-1" integrationId="integration-1" />)

    expect(screen.getByRole('button', { name: 'Ativar coletor' })).toBeEnabled()
  })

  it('desabilita o botão e exibe o motivo quando o plano é inelegível', () => {
    statusMock = {
      status: {
        status: 'Inactive',
        canActivate: false,
        activationBlockReason: 'PlanIneligible',
        credentialValidationStatus: 'NotValidated',
        activatedAtUtc: null,
        deactivatedAtUtc: null,
        lastImmediateCommand: null,
      },
      isLoading: false,
      isError: false,
    }
    render(<CollectorActivationPanel tenantId="tenant-1" integrationId="integration-1" />)

    expect(screen.getByRole('button', { name: 'Ativar coletor' })).toBeDisabled()
    expect(screen.getByText(/n\u00e3o permite ativar o Agente Coletor/)).toBeInTheDocument()
  })

  it('exibe o status ativo sem o botão de ativação', () => {
    statusMock = {
      status: {
        status: 'Active',
        canActivate: true,
        activationBlockReason: 'None',
        credentialValidationStatus: 'Succeeded',
        activatedAtUtc: '2026-09-01T00:00:00Z',
        deactivatedAtUtc: null,
        lastImmediateCommand: null,
      },
      isLoading: false,
      isError: false,
    }
    render(<CollectorActivationPanel tenantId="tenant-1" integrationId="integration-1" />)

    expect(screen.getByText('Ativo')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ativar coletor' })).not.toBeInTheDocument()
  })

  it('aciona a ativação e recarrega o status ao clicar no botão', async () => {
    statusMock = {
      status: {
        status: 'Inactive',
        canActivate: true,
        activationBlockReason: 'None',
        credentialValidationStatus: 'Succeeded',
        activatedAtUtc: null,
        deactivatedAtUtc: null,
        lastImmediateCommand: null,
      },
      isLoading: false,
      isError: false,
    }
    activateMock.mockResolvedValueOnce({ status: 'success' })
    const user = userEvent.setup()
    render(<CollectorActivationPanel tenantId="tenant-1" integrationId="integration-1" />)

    await user.click(screen.getByRole('button', { name: 'Ativar coletor' }))

    expect(activateMock).toHaveBeenCalledTimes(1)
    expect(refetchMock).toHaveBeenCalledTimes(1)
  })
})
