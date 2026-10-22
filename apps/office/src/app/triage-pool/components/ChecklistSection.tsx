import { useTranslation } from 'react-i18next'
import type { TriagemPoolChecklistItemView } from '../../../shared/types/triagePool'
import { translateChecklistItem } from '../lib/triageStatus'
import { Badge } from '../../../shared/components/ui/Badge'

interface ChecklistSectionProps {
  checklist: TriagemPoolChecklistItemView[]
}

/** C2 — checklist mínimo/opcional, com status (completo/pendente) e valor coletado. */
export function ChecklistSection({ checklist }: ChecklistSectionProps) {
  const { t } = useTranslation()

  return (
    <section className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
      <h2 className="mb-3 text-sm font-semibold text-slate-700">{t('triagePoolDetail.checklist.title')}</h2>
      <ul className="space-y-2">
        {checklist.map((item) => (
          <li
            key={item.item}
            className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-slate-100 px-3 py-2 text-sm"
          >
            <div>
              <span className="font-medium text-slate-800">{translateChecklistItem(item.item)}</span>
              {!item.required && (
                <span className="ml-2 text-xs text-slate-400">{t('triagePoolDetail.checklist.optional')}</span>
              )}
              {item.value && <p className="mt-0.5 text-xs text-slate-500">{item.value}</p>}
            </div>
            <Badge tone={item.completed ? 'success' : 'neutral'}>
              {item.completed
                ? t('triagePoolDetail.checklist.completed')
                : t('triagePoolDetail.checklist.pending')}
            </Badge>
          </li>
        ))}
      </ul>
    </section>
  )
}
