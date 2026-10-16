import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TriagemMessageTemplatesSection } from '../../components/TriagemTemplates'
import type { TriagemMessageTemplate } from '../../../../shared/types/triagemMessageTemplate'

const refetchMock = vi.fn()
const saveDraftMock = vi.fn()
const previewMock = vi.fn()
const publishMock = vi.fn()

let templatesMock: TriagemMessageTemplate[]

vi.mock('../../hooks/useTriagemMessageTemplates', () => ({
  useTriagemMessageTemplates: () => ({
    templates: templatesMock,
    isLoading: false,
    isError: false,
    refetch: refetchMock,
  }),
  useTriagemMessageTemplateMutations: () => ({
    saveDraft: saveDraftMock,
    preview: previewMock,
    publish: publishMock,
    isSaving: false,
    isPreviewing: false,
    isPublishing: false,
  }),
}))

describe('TriagemMessageTemplatesSection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    templatesMock = [
      {
        id: 'initial-v1',
        type: 'InitialContact',
        version: 1,
        content: 'Olá, {{nome_posto}}. OS {{numero_os}}.',
        status: 'Published',
        isActive: true,
        createdAtUtc: '2026-10-16T12:00:00Z',
        updatedAtUtc: '2026-10-16T12:00:00Z',
        publishedAtUtc: '2026-10-16T12:00:00Z',
        validationErrors: [],
      },
      {
        id: 'reminder-v1',
        type: 'Reminder24Hours',
        version: 1,
        content: 'Ainda faltam: {{itens_pendentes}}.',
        status: 'Published',
        isActive: true,
        createdAtUtc: '2026-10-16T12:00:00Z',
        updatedAtUtc: '2026-10-16T12:00:00Z',
        publishedAtUtc: '2026-10-16T12:00:00Z',
        validationErrors: [],
      },
    ]
  })

  it('exibe a versão ativa e seu conteúdo', () => {
    render(
      <TriagemMessageTemplatesSection
        tenantId="tenant-1"
        integrationId="integration-1"
        canManage
      />,
    )

    expect(screen.getByText('Versão ativa: v1')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Conteúdo da mensagem' })).toHaveValue(
      'Olá, {{nome_posto}}. OS {{numero_os}}.',
    )
  })

  it('salva alterações como rascunho', async () => {
    saveDraftMock.mockResolvedValueOnce({ status: 'success' })
    const user = userEvent.setup()
    render(
      <TriagemMessageTemplatesSection
        tenantId="tenant-1"
        integrationId="integration-1"
        canManage
      />,
    )

    const editor = screen.getByRole('textbox', { name: 'Conteúdo da mensagem' })
    await user.clear(editor)
    await user.type(editor, 'Nova mensagem')
    await user.click(screen.getByRole('button', { name: 'Salvar rascunho' }))

    expect(saveDraftMock).toHaveBeenCalledWith('InitialContact', 'Nova mensagem')
    expect(refetchMock).toHaveBeenCalledTimes(1)
    expect(await screen.findByText('Rascunho salvo com sucesso.')).toBeInTheDocument()
  })

  it('gera preview sem publicar o template', async () => {
    previewMock.mockResolvedValueOnce({
      status: 'success',
      response: { templates: [], validationErrors: [], preview: 'Mensagem renderizada' },
    })
    const user = userEvent.setup()
    render(
      <TriagemMessageTemplatesSection
        tenantId="tenant-1"
        integrationId="integration-1"
        canManage
      />,
    )

    await user.click(screen.getByRole('button', { name: 'Gerar preview' }))

    expect(previewMock).toHaveBeenCalledWith(
      'InitialContact',
      'Olá, {{nome_posto}}. OS {{numero_os}}.',
    )
    expect(await screen.findByText('Mensagem renderizada')).toBeInTheDocument()
    expect(publishMock).not.toHaveBeenCalled()
  })

  it('mantém o conteúdo somente leitura para operador', () => {
    render(
      <TriagemMessageTemplatesSection
        tenantId="tenant-1"
        integrationId="integration-1"
        canManage={false}
      />,
    )

    expect(screen.getByRole('textbox', { name: 'Conteúdo da mensagem' })).toHaveAttribute(
      'readonly',
    )
    expect(screen.queryByRole('button', { name: 'Salvar rascunho' })).not.toBeInTheDocument()
    expect(
      screen.getByText(
        'Somente proprietários e administradores podem alterar ou publicar templates.',
      ),
    ).toBeInTheDocument()
  })
})
