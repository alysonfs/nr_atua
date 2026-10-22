import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TriagemPoolAttachment } from '../../../shared/types/triagePool'

interface AttachmentItemProps {
  attachment: TriagemPoolAttachment
}

/**
 * Um anexo do pool (C2): imagem em miniatura, áudio/vídeo com player nativo
 * ou link para os demais tipos. A URL é pré-assinada e temporária — a role
 * da EC2 ainda não tem `s3:GetObject` em produção (ver épico, seção 6), então
 * o carregamento pode falhar; trata com mensagem amigável em vez de erro
 * quebrado de mídia.
 */
function AttachmentItem({ attachment }: AttachmentItemProps) {
  const { t } = useTranslation()
  const [failed, setFailed] = useState(false)

  if (failed) {
    return (
      <div className="rounded-md border border-amber-200 bg-amber-50 p-3 text-xs text-amber-800">
        {t('triagePoolDetail.attachments.loadError')}
      </div>
    )
  }

  if (attachment.mediaKind === 'Image') {
    return (
      <a href={attachment.url} target="_blank" rel="noreferrer" className="block">
        <img
          src={attachment.url}
          alt={attachment.caption ?? t('triagePoolDetail.attachments.imageAlt')}
          onError={() => setFailed(true)}
          className="h-28 w-28 rounded-md border border-slate-200 object-cover"
        />
      </a>
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

  return (
    <a
      href={attachment.url}
      target="_blank"
      rel="noreferrer"
      className="inline-flex items-center gap-1 text-sm text-sky-700 hover:underline"
    >
      {attachment.caption ?? t('triagePoolDetail.attachments.documentLink')}
    </a>
  )
}

interface AttachmentsSectionProps {
  attachments: TriagemPoolAttachment[]
}

/** C2 — anexos recebidos do consumidor, com acesso por link autorizado e temporário. */
export function AttachmentsSection({ attachments }: AttachmentsSectionProps) {
  const { t } = useTranslation()

  return (
    <section className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
      <h2 className="mb-3 text-sm font-semibold text-slate-700">{t('triagePoolDetail.attachments.title')}</h2>
      {attachments.length === 0 ? (
        <p className="text-sm text-slate-500">{t('triagePoolDetail.attachments.empty')}</p>
      ) : (
        <div className="flex flex-wrap gap-4">
          {attachments.map((attachment) => (
            <div key={attachment.mediaId} className="space-y-1">
              <AttachmentItem attachment={attachment} />
              {attachment.isVoiceNote && attachment.durationSeconds !== null && (
                <p className="text-xs text-slate-400">
                  {t('triagePoolDetail.attachments.voiceNoteDuration', { seconds: attachment.durationSeconds })}
                </p>
              )}
            </div>
          ))}
        </div>
      )}
    </section>
  )
}
