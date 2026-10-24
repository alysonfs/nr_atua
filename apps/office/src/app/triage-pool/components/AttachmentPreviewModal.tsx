import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Close, Left, Right, ZoomIn, ZoomOut, FullScreen } from '@icon-park/react'
import { Dialog } from '../../../shared/components/ui/Dialog'
import type { TriagemPoolAttachment } from '../../../shared/types/triagePool'
import { PdfPreview } from './PdfPreview'

const MIN_SCALE = 1
const MAX_SCALE = 3
const SCALE_STEP = 0.5

interface AttachmentPreviewModalProps {
  /** Anexo em visualização ampliada; `null` mantém o modal fechado. */
  attachment: TriagemPoolAttachment | null
  /**
   * Anexos de imagem navegáveis (anterior/próximo), na mesma ordem exibida
   * na galeria. Ignorado para anexos que não são imagem (ex.: PDF).
   */
  images: TriagemPoolAttachment[]
  onNavigate: (attachment: TriagemPoolAttachment) => void
  onClose: () => void
}

/**
 * Modal de visualização ampliada de um anexo da triagem (C2), aberto pelo
 * botão de zoom em `AttachmentsSection`.
 *
 * - Imagens: zoom +/- (até 3x) ou "ajustar à tela" (volta a 1x), navegação
 *   anterior/próximo entre as demais imagens da triagem (teclado ou botão),
 *   e `alt` descritivo.
 * - PDFs são renderizados localmente, sem visualizador nativo do navegador.
 * - Fecha com Esc, clique fora do painel (ver `Dialog`) ou botão de fechar.
 */
export function AttachmentPreviewModal({ attachment, images, onNavigate, onClose }: AttachmentPreviewModalProps) {
  const { t } = useTranslation()
  const [scale, setScale] = useState(MIN_SCALE)
  // Zoom volta ao padrão a cada troca de anexo (nova imagem, novo zoom).
  // Ajustado durante a renderização (e não em um efeito) para evitar o
  // "cascading render" de um `setState` síncrono dentro de `useEffect`.
  const [lastMediaId, setLastMediaId] = useState(attachment?.mediaId)
  if (attachment?.mediaId !== lastMediaId) {
    setLastMediaId(attachment?.mediaId)
    setScale(MIN_SCALE)
  }

  const isPdf = attachment?.contentType.split(';')[0].trim().toLowerCase() === 'application/pdf'
  const isImage = attachment?.mediaKind === 'Image' || attachment?.contentType.toLowerCase().startsWith('image/')

  const currentIndex = attachment ? images.findIndex((img) => img.mediaId === attachment.mediaId) : -1
  const hasMultipleImages = isImage && images.length > 1 && currentIndex >= 0

  function goToOffset(offset: number) {
    if (!hasMultipleImages) return
    const nextIndex = (currentIndex + offset + images.length) % images.length
    onNavigate(images[nextIndex])
  }

  useEffect(() => {
    if (!attachment) return

    function handleKeyDown(event: globalThis.KeyboardEvent) {
      if (!hasMultipleImages) return
      if (event.key === 'ArrowLeft') goToOffset(-1)
      if (event.key === 'ArrowRight') goToOffset(1)
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [attachment, hasMultipleImages, currentIndex, images])

  if (!attachment) return null

  const title = isPdf
    ? t('triagePoolDetail.attachments.preview.pdfTitle')
    : t('triagePoolDetail.attachments.preview.imageTitle')

  return (
    <Dialog open={Boolean(attachment)} onClose={onClose} ariaLabel={title} className="flex flex-col">
      <header className="flex items-center justify-between gap-2 border-b border-slate-200 px-4 py-3">
        <h2 className="text-sm font-semibold text-slate-800">
          {title}
          {hasMultipleImages && (
            <span className="ml-2 font-normal text-slate-500">
              {t('triagePoolDetail.attachments.preview.counter', {
                current: currentIndex + 1,
                total: images.length,
              })}
            </span>
          )}
        </h2>
        <button
          type="button"
          onClick={onClose}
          aria-label={t('triagePoolDetail.attachments.preview.close')}
          className="rounded-md p-1.5 text-slate-500 hover:bg-slate-100 hover:text-slate-800"
        >
          <Close theme="outline" size={18} />
        </button>
      </header>

      {isImage && (
        <div className="relative flex h-[70vh] items-center justify-center overflow-auto bg-slate-900/5">
          {hasMultipleImages && (
            <button
              type="button"
              onClick={() => goToOffset(-1)}
              aria-label={t('triagePoolDetail.attachments.preview.previous')}
              className="absolute left-2 top-1/2 -translate-y-1/2 rounded-full bg-white/90 p-2 text-slate-700 shadow hover:bg-white"
            >
              <Left theme="outline" size={20} />
            </button>
          )}
          <img
            src={attachment.url}
            alt={attachment.caption ?? t('triagePoolDetail.attachments.imageAlt')}
            onError={event => {
              event.currentTarget.alt = t('triagePoolDetail.attachments.loadError')
            }}
            style={{ transform: `scale(${scale})`, transformOrigin: 'center center' }}
            className="max-h-full max-w-full object-contain transition-transform"
          />
          {hasMultipleImages && (
            <button
              type="button"
              onClick={() => goToOffset(1)}
              aria-label={t('triagePoolDetail.attachments.preview.next')}
              className="absolute right-2 top-1/2 -translate-y-1/2 rounded-full bg-white/90 p-2 text-slate-700 shadow hover:bg-white"
            >
              <Right theme="outline" size={20} />
            </button>
          )}
        </div>
      )}

      {isPdf && (
        <div className="flex h-[70vh] flex-col gap-2 p-4">
          <PdfPreview key={attachment.mediaId} contentPath={attachment.contentPath} scale={scale} />
        </div>
      )}

      <footer className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-200 px-4 py-3">
        {isImage || isPdf ? (
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => setScale((s) => Math.max(MIN_SCALE, Number((s - SCALE_STEP).toFixed(2))))}
              disabled={scale <= MIN_SCALE}
              aria-label={t('triagePoolDetail.attachments.preview.zoomOut')}
              className="rounded-md p-1.5 text-slate-600 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <ZoomOut theme="outline" size={18} />
            </button>
            <button
              type="button"
              onClick={() => setScale((s) => Math.min(MAX_SCALE, Number((s + SCALE_STEP).toFixed(2))))}
              disabled={scale >= MAX_SCALE}
              aria-label={t('triagePoolDetail.attachments.preview.zoomIn')}
              className="rounded-md p-1.5 text-slate-600 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <ZoomIn theme="outline" size={18} />
            </button>
            <button
              type="button"
              onClick={() => setScale(MIN_SCALE)}
              disabled={scale === MIN_SCALE}
              aria-label={t('triagePoolDetail.attachments.preview.fitToScreen')}
              className="inline-flex items-center gap-1 rounded-md px-2 py-1.5 text-xs font-medium text-slate-600 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <FullScreen theme="outline" size={16} />
              {t('triagePoolDetail.attachments.preview.fitToScreen')}
            </button>
          </div>
        ) : (
          <span />
        )}
        <a
          href={attachment.url}
          target="_blank"
          rel="noreferrer"
          className="text-sm font-medium text-sky-700 hover:underline"
        >
          {t('triagePoolDetail.attachments.preview.openInNewTab')}
        </a>
      </footer>
    </Dialog>
  )
}
