import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { CreateTenantForm } from '../../components/Onboarding/CreateTenantForm'

const createTenantMock = vi.fn()

vi.mock('../../hooks/useTenants', () => ({
  useCreateTenant: () => ({
    createTenant: createTenantMock,
    isSubmitting: false,
  }),
}))

describe('CreateTenantForm', () => {
  it('exibe erros de validação quando nome e CNPJ estão vazios', async () => {
    const user = userEvent.setup()
    render(<CreateTenantForm onCreated={vi.fn()} />)

    await user.click(screen.getByRole('button', { name: 'Criar empresa' }))

    expect(screen.getByText('Informe o nome da empresa.')).toBeInTheDocument()
    expect(screen.getByText('CNPJ inválido. Verifique o número informado.')).toBeInTheDocument()
    expect(createTenantMock).not.toHaveBeenCalled()
  })

  it('exibe erro de CNPJ inválido para CNPJ malformado', async () => {
    const user = userEvent.setup()
    render(<CreateTenantForm onCreated={vi.fn()} />)

    await user.type(screen.getByLabelText('Nome da empresa'), 'Minha Empresa')
    await user.type(screen.getByLabelText('CNPJ'), '111.111.111/1111-11')
    await user.click(screen.getByRole('button', { name: 'Criar empresa' }))

    expect(screen.getByText('CNPJ inválido. Verifique o número informado.')).toBeInTheDocument()
    expect(createTenantMock).not.toHaveBeenCalled()
  })

  it('exibe erro de conflito quando o CNPJ já está cadastrado', async () => {
    createTenantMock.mockResolvedValueOnce({
      status: 'error',
      errorCode: 'cnpj_already_registered',
    })
    const user = userEvent.setup()
    render(<CreateTenantForm onCreated={vi.fn()} />)

    await user.type(screen.getByLabelText('Nome da empresa'), 'Minha Empresa')
    await user.type(screen.getByLabelText('CNPJ'), '11.444.777/0001-61')
    await user.click(screen.getByRole('button', { name: 'Criar empresa' }))

    await waitFor(() => {
      expect(
        screen.getByText('Este CNPJ já está associado a outra empresa.'),
      ).toBeInTheDocument()
    })
  })

  it('chama onCreated com o tenantId e integrationId quando a criação é bem-sucedida', async () => {
    createTenantMock.mockResolvedValueOnce({
      status: 'success',
      tenantId: 'tenant-123',
      integrationId: 'integration-456',
    })
    const onCreated = vi.fn()
    const user = userEvent.setup()
    render(<CreateTenantForm onCreated={onCreated} />)

    await user.type(screen.getByLabelText('Nome da empresa'), 'Minha Empresa')
    await user.type(screen.getByLabelText('CNPJ'), '11.444.777/0001-61')
    await user.click(screen.getByRole('button', { name: 'Criar empresa' }))

    await waitFor(() => {
      expect(onCreated).toHaveBeenCalledWith('tenant-123', 'integration-456')
    })
  })
})
