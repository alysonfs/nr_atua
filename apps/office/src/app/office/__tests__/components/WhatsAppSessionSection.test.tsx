import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { WhatsAppSessionSection } from '../../components/WhatsApp'
import type { WhatsAppSessionStateResponse } from '../../../../shared/types/whatsAppSession'
import type { WhatsAppSessionErrorCode } from '../../../../shared/types/whatsAppSession'

let sessionMock: WhatsAppSessionStateResponse | null
let isLoadingMock: boolean
let errorCodeMock: WhatsAppSessionErrorCode | null
const refetchMock = vi.fn()

vi.mock('../../hooks/useWhatsAppSession', () => ({
  useWhatsAppSession: () => ({
    session: sessionMock,
    isLoading: isLoadingMock,
    errorCode: errorCodeMock,
    refetch: refetchMock,
  }),
}))

describe('WhatsAppSessionSection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionMock = null
    isLoadingMock = false
    errorCodeMock = null
  })

  it('não exibe conteúdo para quem não é OWNER/ADMIN', () => {
    render(
      <WhatsAppSessionSection tenantId="tenant-1" integrationId="integration-1" canManage={false} />,
    )

    expect(screen.getByText('Sem permissão')).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('mostra carregando enquanto a consulta inicial está em andamento', () => {
    isLoadingMock = true
    render(
      <WhatsAppSessionSection tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    expect(screen.getByText('Carregando estado da sessão...')).toBeInTheDocument()
  })

  it('exibe "Sem permissão" quando a API responde 403', () => {
    errorCodeMock = 'forbidden'
    render(
      <WhatsAppSessionSection tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('Sem permissão')
  })

  it('trata integration_id_required com mensagem específica', () => {
    errorCodeMock = 'integration_id_required'
    render(
      <WhatsAppSessionSection tenantId="tenant-1" integrationId={null} canManage />,
    )

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Há mais de uma integração de WhatsApp para esta empresa.',
    )
  })

  it('estado unknown: canal não conectado', () => {
    sessionMock = { state: 'unknown' }
    render(
      <WhatsAppSessionSection tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    expect(screen.getByText('Canal não conectado/aguardando o serviço')).toBeInTheDocument()
  })

  it('estado qr_pending: renderiza o QR como imagem e a contagem regressiva', () => {
    const qrExpiresAtUtc = new Date(Date.now() + 42_000).toISOString()
    sessionMock = {
      state: 'qr_pending',
      qr: '2@abc123,raw-qr-payload',
      qrExpiresAtUtc,
      updatedAtUtc: new Date().toISOString(),
    }
    render(
      <WhatsAppSessionSection tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    expect(
      screen.getAllByText('Escaneie o QR no WhatsApp > Aparelhos conectados'),
    ).toHaveLength(2)
    expect(screen.getByRole('img', { name: 'QR Code para parear o WhatsApp' })).toBeInTheDocument()
    expect(screen.getByText(/Expira em \d+s/)).toBeInTheDocument()
  })

  it('estado qr_pending sem QR (expirado): mostra aguardando novo QR', () => {
    sessionMock = {
      state: 'qr_pending',
      qr: null,
      qrExpiresAtUtc: new Date(Date.now() - 1_000).toISOString(),
    }
    render(
      <WhatsAppSessionSection tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    expect(screen.getByText('QR expirado — aguardando novo QR.')).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('estado connected: mostra o telefone pareado', () => {
    sessionMock = { state: 'connected', phoneNumber: '5584999999999' }
    render(
      <WhatsAppSessionSection tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    expect(screen.getByText('Conectado (5584999999999)')).toBeInTheDocument()
  })

  it('estado disconnected: reconectando', () => {
    sessionMock = { state: 'disconnected' }
    render(
      <WhatsAppSessionSection tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    expect(screen.getByText('Reconectando…')).toBeInTheDocument()
  })

  it('estado logged_out: sessão encerrada', () => {
    sessionMock = { state: 'logged_out' }
    render(
      <WhatsAppSessionSection tenantId="tenant-1" integrationId="integration-1" canManage />,
    )

    expect(screen.getByText('Sessão encerrada — aguarde novo QR')).toBeInTheDocument()
  })
})
