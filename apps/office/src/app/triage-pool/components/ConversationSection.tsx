import { useTranslation } from 'react-i18next'
import type { TriagemPoolConversationEntry } from '../../../shared/types/triagePool'
import { translateConversationSender } from '../lib/triageStatus'

interface ConversationSectionProps {
  conversation: TriagemPoolConversationEntry[]
  locale: string
}

function formatDateTime(isoDate: string, locale: string): string {
  return new Date(isoDate).toLocaleString(locale, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

/** C2 — conversa em ordem cronológica, incluindo transcrições quando existirem. */
export function ConversationSection({ conversation, locale }: ConversationSectionProps) {
  const { t } = useTranslation()

  return (
    <section className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
      <h2 className="mb-3 text-sm font-semibold text-slate-700">{t('triagePoolDetail.conversation.title')}</h2>
      {conversation.length === 0 ? (
        <p className="text-sm text-slate-500">{t('triagePoolDetail.conversation.empty')}</p>
      ) : (
        <ol className="space-y-3">
          {conversation.map((entry, index) => (
            <li key={`${entry.atUtc}-${index}`} className="rounded-md border border-slate-100 p-3 text-sm">
              <div className="mb-1 flex items-center justify-between text-xs text-slate-500">
                <span className="font-semibold text-slate-700">{translateConversationSender(t, entry.sender)}</span>
                <span>{formatDateTime(entry.atUtc, locale)}</span>
              </div>
              <p className="text-slate-800">
                {entry.text ?? t('triagePoolDetail.conversation.attachmentOnly')}
              </p>
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}
