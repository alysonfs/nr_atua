import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { IServiceValidationStatus } from '../../components/Integrations/IServiceValidationStatus'

const validateMock = vi.fn()
const refetchMock = vi.fn()

let credentialsStatusMock: {
  status: {
    hasCredentials: boolean
    validationStatus: 'NotValidated' | 'Succeeded' | 'Failed'
    lastValidatedAtUtc: string | null
    updatedAtUtc: string | null
  } | null
  isLoading: boolean
  isError: boolean
}

vi.mock('../../hooks/useIServiceIntegration', () => ({
  useIServiceCredentials: () => ({
    ...credentialsStatusMock,
    refetch: refetchMock,
  }),
  useValidateIServiceCredentials: () => ({
    validate: validateMock,
    isValidating: false,
  }),
}))

describe('IServiceValidationStatus', () => {
  it('exibe estado de carregamento', () => {
    credentialsStatusMock = { status: null, isLoading: true, isError: false }
    render(<IServiceValidationStatus tenantId="tenant-1" integrationId="integration-1" />)

    expect(screen.getByText('Carregando status da integração...')).toBeInTheDocument()
  })

  it('exibe erro quando a leitura do status falha', () => {
    credentialsStatusMock = { status: null, isLoading: false, isError: true }
    render(<IServiceValidationStatus tenantId="tenant-1" integrationId="integration-1" />)

    expect(
      screen.getByText('Não foi possível carregar o status da integração. Tente novamente mais tarde.'),
    ).toBeInTheDocument()
  })

  it('informa que não há credenciais configuradas', () => {
    credentialsStatusMock = {
      status: {
        hasCredentials: false,
        validationStatus: 'NotValidated',
        lastValidatedAtUtc: null,
        updatedAtUtc: null,
      },
      isLoading: false,
      isError: false,
    }
    render(<IServiceValidationStatus tenantId="tenant-1" integrationId="integration-1" />)

    expect(
      screen.getByText(/Nenhuma credencial configurada ainda/),
    ).toBeInTheDocument()
  })

  it('bloqueia a ativação (não exibe sucesso) quando nunca validado', () => {
    credentialsStatusMock = {
      status: {
        hasCredentials: true,
        validationStatus: 'NotValidated',
        lastValidatedAtUtc: null,
        updatedAtUtc: '2026-08-29T00:00:00Z',
      },
      isLoading: false,
      isError: false,
    }
    render(<IServiceValidationStatus tenantId="tenant-1" integrationId="integration-1" />)

    expect(screen.getByText('Não validado')).toBeInTheDocument()
    expect(
      screen.getByText(/ainda não foi validada/),
    ).toBeInTheDocument()
  })

  it('exibe mensagem de falha e permite nova tentativa quando a validação falha', async () => {
    credentialsStatusMock = {
      status: {
        hasCredentials: true,
        validationStatus: 'Failed',
        lastValidatedAtUtc: '2026-08-29T00:00:00Z',
        updatedAtUtc: '2026-08-29T00:00:00Z',
      },
      isLoading: false,
      isError: false,
    }
    validateMock.mockResolvedValueOnce({
      status: 'success',
      validationStatus: 'Failed',
      evaluatedAtUtc: '2026-08-29T01:00:00Z',
    })
    const user = userEvent.setup()
    render(<IServiceValidationStatus tenantId="tenant-1" integrationId="integration-1" />)

    expect(screen.getByText('Falha na validação')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Testar validação' }))

    await waitFor(() => {
      expect(
        screen.getByText('A validação falhou. Verifique as credenciais cadastradas e tente novamente.'),
      ).toBeInTheDocument()
    })
    expect(refetchMock).toHaveBeenCalled()
  })

  it('exibe sucesso quando a validação sob demanda é bem-sucedida', async () => {
    credentialsStatusMock = {
      status: {
        hasCredentials: true,
        validationStatus: 'Succeeded',
        lastValidatedAtUtc: '2026-08-29T00:00:00Z',
        updatedAtUtc: '2026-08-29T00:00:00Z',
      },
      isLoading: false,
      isError: false,
    }
    validateMock.mockResolvedValueOnce({
      status: 'success',
      validationStatus: 'Succeeded',
      evaluatedAtUtc: '2026-08-29T01:00:00Z',
    })
    const user = userEvent.setup()
    render(<IServiceValidationStatus tenantId="tenant-1" integrationId="integration-1" />)

    await user.click(screen.getByRole('button', { name: 'Testar validação' }))

    await waitFor(() => {
      expect(screen.getByText('Credenciais validadas com sucesso.')).toBeInTheDocument()
    })
  })

  it('nunca exibe dados sensíveis (senha) na tela de status', () => {
    credentialsStatusMock = {
      status: {
        hasCredentials: true,
        validationStatus: 'Succeeded',
        lastValidatedAtUtc: '2026-08-29T00:00:00Z',
        updatedAtUtc: '2026-08-29T00:00:00Z',
      },
      isLoading: false,
      isError: false,
    }
    const { container } = render(
      <IServiceValidationStatus tenantId="tenant-1" integrationId="integration-1" />,
    )

    expect(container.querySelector('input[type="password"]')).not.toBeInTheDocument()
  })
})
