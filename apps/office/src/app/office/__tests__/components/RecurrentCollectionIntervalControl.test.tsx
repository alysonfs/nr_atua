import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RecurrentCollectionIntervalControl } from '../../components/Integrations/RecurrentCollectionIntervalControl'
import type { RecurrentCollectionIntervalView } from '../../../../shared/types/integration'

const updateMock = vi.fn()
const refetchMock = vi.fn()

let intervalMock: {
  interval: RecurrentCollectionIntervalView | null
  isLoading: boolean
  isError: boolean
}

vi.mock('../../hooks/useRecurrentCollectionInterval', () => ({
  useRecurrentCollectionInterval: () => ({
    ...intervalMock,
    refetch: refetchMock,
  }),
  useUpdateRecurrentCollectionInterval: () => ({
    update: updateMock,
    isSaving: false,
  }),
}))

describe('RecurrentCollectionIntervalControl', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    intervalMock = { interval: { recurrentCollectionIntervalMinutes: 15 }, isLoading: false, isError: false }
  })

  it('exibe o valor atual do intervalo em minutos', () => {
    render(
      <RecurrentCollectionIntervalControl tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    expect(screen.getByRole('heading', { name: 'Frequência da coleta' })).toBeInTheDocument()
    expect(screen.getByRole('spinbutton')).toHaveValue(15)
  })

  it('salva um novo intervalo válido', async () => {
    updateMock.mockResolvedValueOnce({
      status: 'success',
      view: { recurrentCollectionIntervalMinutes: 30 },
    })
    const user = userEvent.setup()
    render(
      <RecurrentCollectionIntervalControl tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    const input = screen.getByRole('spinbutton')
    await user.clear(input)
    await user.type(input, '30')
    await user.click(screen.getByRole('button', { name: 'Salvar' }))

    expect(updateMock).toHaveBeenCalledWith(30)
    expect(refetchMock).toHaveBeenCalledTimes(1)
    expect(await screen.findByText('Intervalo de coleta atualizado com sucesso.')).toBeInTheDocument()
  })

  it('rejeita localmente um intervalo abaixo do mínimo sem chamar a API', async () => {
    const user = userEvent.setup()
    render(
      <RecurrentCollectionIntervalControl tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    const input = screen.getByRole('spinbutton')
    await user.clear(input)
    await user.type(input, '2')
    await user.click(screen.getByRole('button', { name: 'Salvar' }))

    expect(screen.getByText('Informe um intervalo entre 5 e 1440 minutos.')).toBeInTheDocument()
    expect(updateMock).not.toHaveBeenCalled()
  })

  it('rejeita localmente um intervalo acima do máximo sem chamar a API', async () => {
    const user = userEvent.setup()
    render(
      <RecurrentCollectionIntervalControl tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    const input = screen.getByRole('spinbutton')
    await user.clear(input)
    await user.type(input, '2000')
    await user.click(screen.getByRole('button', { name: 'Salvar' }))

    expect(screen.getByText('Informe um intervalo entre 5 e 1440 minutos.')).toBeInTheDocument()
    expect(updateMock).not.toHaveBeenCalled()
  })

  it('exibe mensagem de erro quando o salvamento falha', async () => {
    updateMock.mockResolvedValueOnce({ status: 'error', errorCode: 'forbidden' })
    const user = userEvent.setup()
    render(
      <RecurrentCollectionIntervalControl tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    const input = screen.getByRole('spinbutton')
    await user.clear(input)
    await user.type(input, '30')
    await user.click(screen.getByRole('button', { name: 'Salvar' }))

    expect(
      screen.getByText('Apenas o proprietário ou administrador da empresa pode alterar o intervalo de coleta.'),
    ).toBeInTheDocument()
    expect(refetchMock).not.toHaveBeenCalled()
  })

  it('não é exibido para papéis não autorizados', () => {
    const { container } = render(
      <RecurrentCollectionIntervalControl
        tenantId="tenant-1"
        integrationId="integration-1"
        canManage={false}
      />,
    )

    expect(container).toBeEmptyDOMElement()
  })

  it('não é exibido quando não há integração provisionada', () => {
    const { container } = render(
      <RecurrentCollectionIntervalControl tenantId="tenant-1" integrationId={null} canManage />,
    )

    expect(container).toBeEmptyDOMElement()
  })
})
