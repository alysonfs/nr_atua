import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { PreviewOpen } from '@icon-park/react'
import type { TriagemPoolAttachment } from '../../../shared/types/triagePool'
import { AttachmentPreviewModal } from './AttachmentPreviewModal'
import { PdfPreview } from './PdfPreview'

interface AttachmentItemProps {
  attachment: TriagemPoolAttachment
  onPreview: (attachment: TriagemPoolAttachment) => void
}

/**
 * Um anexo do pool (C2): imagem em miniatura, áudio/vídeo com player nativo
 * ou link para os demais tipos. PDFs são renderizados localmente a partir
 * da rota autenticada da API, sem depender de plugins do navegador.
 *
 * Imagens e PDFs (`contentType === 'application/pdf'`) ganham um botão de
 * zoom que abre `AttachmentPreviewModal` na própria página.
 */
function AttachmentItem({ attachment, onPreview }: AttachmentItemProps) {
  const { t } = useTranslation()
  const [failed, setFailed] = useState(false)

  if (failed) {
    return (
      <div className="rounded-md border border-amber-200 bg-amber-50 p-3 text-xs text-amber-800">
        {t('triagePoolDetail.attachments.loadError')}
      </div>
    )
  }

  if (attachment.mediaKind === 'Image' || attachment.contentType.toLowerCase().startsWith('image/')) {
    return (
      <div className="group relative">
        <img
          src={attachment.url}
          alt={attachment.caption ?? t('triagePoolDetail.attachments.imageAlt')}
          onError={() => setFailed(true)}
          className="h-28 w-28 rounded-md border border-slate-200 object-cover"
        />
        <button
          type="button"
          onClick={() => onPreview(attachment)}
          aria-label={t('triagePoolDetail.attachments.zoomAria')}
          className="absolute bottom-1 right-1 rounded-full bg-white/90 p-1.5 text-slate-700 shadow hover:bg-white focus:opacity-100 group-hover:opacity-100 sm:opacity-80"
        >
          <PreviewOpen theme="outline" size={16} />
        </button>
      </div>
    )
  }

  if (attachment.mediaKind === 'Audio') {
    return (
      <audio controls onError={() => setFailed(true)} className="w-full max-w-xs">
        <source src={attachment.url} type={attachment.contentType} />
      </audio>
    )
  }

  if (attachment.mediaKind === 'Video') {
    return (
      <video controls onError={() => setFailed(true)} className="max-h-48 max-w-xs rounded-md">
        <source src={attachment.url} type={attachment.contentType} />
      </video>
    )
  }

  const isPdf = attachment.contentType.split(';')[0].trim().toLowerCase() === 'application/pdf'

  return (
    <div className="flex items-center gap-2">
      {isPdf && (
        <button type="button" onClick={() => onPreview(attachment)} aria-label={t('triagePoolDetail.attachments.zoomAria')}>
          <PdfPreview key={attachment.contentPath} contentPath={attachment.contentPath} thumbnail />
        </button>
      )}
      <a
        href={attachment.url}
        target="_blank"
        rel="noreferrer"
        className="inline-flex items-center gap-1 text-sm text-sky-700 hover:underline"
      >
        {attachment.caption ?? t('triagePoolDetail.attachments.documentLink')}
      </a>
      {isPdf && (
        <button
          type="button"
          onClick={() => onPreview(attachment)}
          aria-label={t('triagePoolDetail.attachments.preview.pdfTitle')}
          className="rounded-md p-1 text-slate-500 hover:bg-slate-100 hover:text-slate-800"
        >
          <PreviewOpen theme="outline" size={16} />
        </button>
      )}
    </div>
  )
}

interface AttachmentsSectionProps {
  attachments: TriagemPoolAttachment[]
}

/** C2 — anexos recebidos do consumidor, com acesso por link autorizado e temporário. */
export function AttachmentsSection({ attachments }: AttachmentsSectionProps) {
  const { t } = useTranslation()
  const [previewAttachment, setPreviewAttachment] = useState<TriagemPoolAttachment | null>(null)

  const imageAttachments = attachments.filter((attachment) =>
    attachment.mediaKind === 'Image' || attachment.contentType.toLowerCase().startsWith('image/'))

  return (
    <section className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
      <h2 className="mb-3 text-sm font-semibold text-slate-700">{t('triagePoolDetail.attachments.title')}</h2>
      {attachments.length === 0 ? (
        <p className="text-sm text-slate-500">{t('triagePoolDetail.attachments.empty')}</p>
      ) : (
        <div className="flex flex-wrap gap-4">
          {attachments.map((attachment) => (
            <div key={attachment.mediaId} className="space-y-1">
              <AttachmentItem attachment={attachment} onPreview={setPreviewAttachment} />
              {attachment.isVoiceNote && attachment.durationSeconds !== null && (
                <p className="text-xs text-slate-400">
                  {t('triagePoolDetail.attachments.voiceNoteDuration', { seconds: attachment.durationSeconds })}
                </p>
              )}
            </div>
          ))}
        </div>
      )}
      <AttachmentPreviewModal
        attachment={previewAttachment}
        images={imageAttachments}
        onNavigate={setPreviewAttachment}
        onClose={() => setPreviewAttachment(null)}
      />
    </section>
  )
}
