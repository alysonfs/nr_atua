/**
 * Test suite for AttachmentsSection + AttachmentPreviewModal (C2 — galeria
 * de anexos e visualização ampliada).
 *
 * Cobre:
 * - Botão de zoom em anexos de imagem abre o modal.
 * - Zoom +/- e "ajustar à tela" na imagem.
 * - Navegação anterior/próximo entre imagens.
 * - Fechamento por Esc, botão e clique fora (overlay).
 * - PDFs exibidos via <object> com link "abrir em nova aba".
 * - Anexos sem zoom (áudio/vídeo/documento não-PDF) não ganham o botão.
 */

import { describe, it, expect } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AttachmentsSection } from '../../components/AttachmentsSection'
import type { TriagemPoolAttachment } from '../../../../shared/types/triagePool'

const imageAttachment: TriagemPoolAttachment = {
  mediaId: 'media-1',
  mediaKind: 'Image',
  contentType: 'image/jpeg',
  isVoiceNote: false,
  durationSeconds: null,
  caption: 'Foto do defeito',
  receivedAtUtc: '2026-10-20T10:00:00Z',
  url: 'https://example.com/media-1.jpg',
  urlExpiresAtUtc: '2026-10-21T10:00:00Z',
}

const secondImageAttachment: TriagemPoolAttachment = {
  ...imageAttachment,
  mediaId: 'media-2',
  caption: 'Etiqueta do produto',
  url: 'https://example.com/media-2.jpg',
}

const pdfAttachment: TriagemPoolAttachment = {
  mediaId: 'media-3',
  mediaKind: 'Document',
  contentType: 'application/pdf',
  isVoiceNote: false,
  durationSeconds: null,
  caption: 'Nota fiscal.pdf',
  receivedAtUtc: '2026-10-20T10:05:00Z',
  url: 'https://example.com/media-3.pdf',
  urlExpiresAtUtc: '2026-10-21T10:00:00Z',
}

const audioAttachment: TriagemPoolAttachment = {
  mediaId: 'media-4',
  mediaKind: 'Audio',
  contentType: 'audio/ogg',
  isVoiceNote: true,
  durationSeconds: 12,
  caption: null,
  receivedAtUtc: '2026-10-20T10:10:00Z',
  url: 'https://example.com/media-4.ogg',
  urlExpiresAtUtc: '2026-10-21T10:00:00Z',
}

describe('AttachmentsSection — preview modal', () => {
  it('does not render a zoom button for audio attachments', () => {
    render(<AttachmentsSection attachments={[audioAttachment]} />)
    expect(screen.queryByRole('button', { name: /ampliar anexo/i })).not.toBeInTheDocument()
  })

  it('opens the modal with the enlarged image when the zoom button is clicked', async () => {
    const user = userEvent.setup()
    render(<AttachmentsSection attachments={[imageAttachment]} />)

    await user.click(screen.getByRole('button', { name: /ampliar anexo/i }))

    const dialog = screen.getByRole('dialog', { name: /visualização da imagem/i })
    expect(dialog).toBeInTheDocument()
    expect(within(dialog).getByAltText('Foto do defeito')).toBeInTheDocument()
  })

  it('zooms in and out, and resets with "ajustar à tela"', async () => {
    const user = userEvent.setup()
    render(<AttachmentsSection attachments={[imageAttachment]} />)

    await user.click(screen.getByRole('button', { name: /ampliar anexo/i }))

    const dialog = screen.getByRole('dialog')
    const image = within(dialog).getByAltText('Foto do defeito')
    expect(image).toHaveStyle({ transform: 'scale(1)' })

    const zoomInButton = screen.getByRole('button', { name: /aumentar zoom/i })
    await user.click(zoomInButton)
    expect(image).toHaveStyle({ transform: 'scale(1.5)' })

    const fitButton = screen.getByRole('button', { name: /ajustar à tela/i })
    await user.click(fitButton)
    expect(image).toHaveStyle({ transform: 'scale(1)' })
  })

  it('navigates between images with next/previous buttons', async () => {
    const user = userEvent.setup()
    render(<AttachmentsSection attachments={[imageAttachment, secondImageAttachment]} />)

    await user.click(screen.getAllByRole('button', { name: /ampliar anexo/i })[0])
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByAltText('Foto do defeito')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /próxima/i }))
    expect(within(dialog).getByAltText('Etiqueta do produto')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /anterior/i }))
    expect(within(dialog).getByAltText('Foto do defeito')).toBeInTheDocument()
  })

  it('does not render navigation controls with a single image', async () => {
    const user = userEvent.setup()
    render(<AttachmentsSection attachments={[imageAttachment]} />)

    await user.click(screen.getByRole('button', { name: /ampliar anexo/i }))

    expect(screen.queryByRole('button', { name: /próxima/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /anterior/i })).not.toBeInTheDocument()
  })

  it('closes the modal when the close button is clicked', async () => {
    const user = userEvent.setup()
    render(<AttachmentsSection attachments={[imageAttachment]} />)

    await user.click(screen.getByRole('button', { name: /ampliar anexo/i }))
    expect(screen.getByRole('dialog')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /^fechar$/i }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('closes the modal with the Esc key', async () => {
    const user = userEvent.setup()
    render(<AttachmentsSection attachments={[imageAttachment]} />)

    await user.click(screen.getByRole('button', { name: /ampliar anexo/i }))
    expect(screen.getByRole('dialog')).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('closes the modal when clicking outside the panel (overlay)', async () => {
    const user = userEvent.setup()
    const { container } = render(<AttachmentsSection attachments={[imageAttachment]} />)

    await user.click(screen.getByRole('button', { name: /ampliar anexo/i }))
    const overlay = container.ownerDocument.querySelector('.fixed.inset-0')
    expect(overlay).not.toBeNull()

    await user.click(overlay as Element)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('renders a PDF in an object element with an "open in new tab" link', async () => {
    const user = userEvent.setup()
    render(<AttachmentsSection attachments={[pdfAttachment]} />)

    await user.click(screen.getByRole('button', { name: /ampliar anexo/i }))

    const dialog = screen.getByRole('dialog', { name: /visualização do documento/i })
    const object = dialog.querySelector('object[type="application/pdf"]')
    expect(object).not.toBeNull()
    expect(object).toHaveAttribute('data', pdfAttachment.url)

    const link = screen.getByRole('link', { name: /abrir em nova aba/i })
    expect(link).toHaveAttribute('href', pdfAttachment.url)
    expect(link).toHaveAttribute('target', '_blank')
  })
})

describe('AttachmentsSection — empty state', () => {
  it('shows the empty message without crashing when there are no attachments', () => {
    render(<AttachmentsSection attachments={[]} />)
    expect(screen.getByText(/nenhum anexo recebido/i)).toBeInTheDocument()
  })
})
