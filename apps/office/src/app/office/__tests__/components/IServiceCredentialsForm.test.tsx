import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { IServiceCredentialsForm } from '../../components/Integrations/IServiceCredentialsForm'

const setCredentialsMock = vi.fn()

vi.mock('../../hooks/useIServiceIntegration', () => ({
  useSetIServiceCredentials: () => ({
    setCredentials: setCredentialsMock,
    isSubmitting: false,
  }),
}))

describe('IServiceCredentialsForm', () => {
  it('exibe erros de validação quando usuário e senha estão vazios', async () => {
    const user = userEvent.setup()
    render(
      <IServiceCredentialsForm tenantId="tenant-1" integrationId="integration-1" onSaved={vi.fn()} />,
    )

    await user.click(screen.getByRole('button', { name: 'Salvar credenciais' }))

    expect(screen.getByText('Informe o usuário do iService.')).toBeInTheDocument()
    expect(screen.getByText('Informe a senha do iService.')).toBeInTheDocument()
    expect(setCredentialsMock).not.toHaveBeenCalled()
  })

  it('exibe erro de autorização quando o backend recusa (usuário não OWNER)', async () => {
    setCredentialsMock.mockResolvedValueOnce({ status: 'error', errorCode: 'forbidden' })
    const user = userEvent.setup()
    render(
      <IServiceCredentialsForm tenantId="tenant-1" integrationId="integration-1" onSaved={vi.fn()} />,
    )

    await user.type(screen.getByLabelText('Usuário'), 'usuario.iservice')
    await user.type(screen.getByLabelText('Senha'), 'senha-secreta')
    await user.click(screen.getByRole('button', { name: 'Salvar credenciais' }))

    await waitFor(() => {
      expect(
        screen.getByText('Apenas o proprietário da empresa pode configurar esta integração.'),
      ).toBeInTheDocument()
    })
  })

  it('limpa os campos e nunca reexibe o segredo após salvar com sucesso', async () => {
    setCredentialsMock.mockResolvedValueOnce({ status: 'success' })
    const onSaved = vi.fn()
    const user = userEvent.setup()
    render(
      <IServiceCredentialsForm tenantId="tenant-1" integrationId="integration-1" onSaved={onSaved} />,
    )

    const usernameInput = screen.getByLabelText('Usuário') as HTMLInputElement
    const passwordInput = screen.getByLabelText('Senha') as HTMLInputElement

    await user.type(usernameInput, 'usuario.iservice')
    await user.type(passwordInput, 'senha-secreta')
    await user.click(screen.getByRole('button', { name: 'Salvar credenciais' }))

    await waitFor(() => {
      expect(onSaved).toHaveBeenCalled()
    })

    expect(usernameInput.value).toBe('')
    expect(passwordInput.value).toBe('')
    expect(screen.queryByDisplayValue('senha-secreta')).not.toBeInTheDocument()
  })

  it('envia o payload correto ao backend, incluindo baseUrl opcional', async () => {
    setCredentialsMock.mockResolvedValueOnce({ status: 'success' })
    const user = userEvent.setup()
    render(
      <IServiceCredentialsForm tenantId="tenant-1" integrationId="integration-1" onSaved={vi.fn()} />,
    )

    await user.type(screen.getByLabelText('Usuário'), 'usuario.iservice')
    await user.type(screen.getByLabelText('Senha'), 'senha-secreta')
    await user.type(screen.getByLabelText(/URL\/tenant do iService/), 'https://cliente.iservice.com')
    await user.click(screen.getByRole('button', { name: 'Salvar credenciais' }))

    await waitFor(() => {
      expect(setCredentialsMock).toHaveBeenCalledWith({
        username: 'usuario.iservice',
        password: 'senha-secreta',
        baseUrl: 'https://cliente.iservice.com',
      })
    })
  })
})
