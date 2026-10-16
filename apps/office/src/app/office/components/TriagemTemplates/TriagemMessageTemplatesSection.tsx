import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type {
  TriagemMessageTemplate,
  TriagemMessageTemplateType,
} from '../../../../shared/types/triagemMessageTemplate'
import {
  useTriagemMessageTemplateMutations,
  useTriagemMessageTemplates,
} from '../../hooks/useTriagemMessageTemplates'

interface TriagemMessageTemplatesSectionProps {
  tenantId: string
  integrationId: string
  canManage: boolean
}

const templateTypes: TriagemMessageTemplateType[] = [
  'InitialContact',
  'Reminder24Hours',
  'Reminder36Hours',
]

export function TriagemMessageTemplatesSection({
  tenantId,
  integrationId,
  canManage,
}: TriagemMessageTemplatesSectionProps) {
  const { t } = useTranslation()
  const { templates, isLoading, isError, refetch } = useTriagemMessageTemplates(
    tenantId,
    integrationId,
  )
  const { saveDraft, preview, publish, isSaving, isPreviewing, isPublishing } =
    useTriagemMessageTemplateMutations(tenantId, integrationId)
  const [selectedType, setSelectedType] =
    useState<TriagemMessageTemplateType>('InitialContact')
  const [edits, setEdits] = useState<Partial<Record<TriagemMessageTemplateType, string>>>({})
  const [previewContent, setPreviewContent] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<string | null>(null)

  const versions = useMemo(
    () =>
      templates
        .filter((template) => template.type === selectedType)
        .sort((left, right) => right.version - left.version),
    [selectedType, templates],
  )
  const draft = versions.find((template) => template.status === 'Draft')
  const active = versions.find((template) => template.isActive)
  const content = edits[selectedType] ?? draft?.content ?? active?.content ?? ''

  const handleSave = async () => {
    setFeedback(null)
    const result = await saveDraft(selectedType, content)
    if (result.status === 'success') {
      setFeedback(t('triageTemplates.feedback.draftSaved'))
      refetch()
      return
    }
    setFeedback(t(`triageTemplates.errors.${result.errorCode ?? 'unknown_error'}`))
  }

  const handlePreview = async () => {
    setFeedback(null)
    const result = await preview(selectedType, content)
    if (result.status === 'success') {
      setPreviewContent(result.response?.preview ?? null)
      return
    }
    setPreviewContent(null)
    setFeedback(t(`triageTemplates.errors.${result.errorCode ?? 'unknown_error'}`))
  }

  const handlePublish = async () => {
    setFeedback(null)
    const result = await publish(selectedType)
    if (result.status === 'success') {
      setFeedback(t('triageTemplates.feedback.published'))
      setPreviewContent(null)
      setEdits((current) => {
        const next = { ...current }
        delete next[selectedType]
        return next
      })
      refetch()
      return
    }
    setFeedback(t(`triageTemplates.errors.${result.errorCode ?? 'unknown_error'}`))
  }

  if (isLoading) {
    return <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">{t('triageTemplates.loading')}</p>
  }

  if (isError) {
    return <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">{t('triageTemplates.loadError')}</p>
  }

  return (
    <div className="rounded-md border border-slate-200 bg-white shadow-sm">
      <div className="border-b border-slate-200 p-4 sm:p-5">
        <div className="flex flex-wrap gap-2" role="tablist" aria-label={t('triageTemplates.typesLabel')}>
          {templateTypes.map((type) => (
            <button
              key={type}
              type="button"
              role="tab"
              aria-selected={selectedType === type}
              onClick={() => {
                setSelectedType(type)
                setPreviewContent(null)
                setFeedback(null)
              }}
              className={`rounded-md px-3 py-2 text-sm font-medium transition ${
                selectedType === type
                  ? 'bg-slate-900 text-white'
                  : 'border border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
              }`}
            >
              {t(`triageTemplates.types.${type}`)}
            </button>
          ))}
        </div>
      </div>

      <div className="grid gap-5 p-4 sm:p-5 xl:grid-cols-[minmax(0,1fr)_320px]">
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-2 text-xs">
            {active && (
              <span className="rounded-full bg-emerald-100 px-2.5 py-1 font-semibold text-emerald-800">
                {t('triageTemplates.activeVersion', { version: active.version })}
              </span>
            )}
            {draft && (
              <span className="rounded-full bg-amber-100 px-2.5 py-1 font-semibold text-amber-800">
                {t('triageTemplates.draftVersion', { version: draft.version })}
              </span>
            )}
          </div>

          <label className="block text-sm font-medium text-slate-800" htmlFor="triage-template-content">
            {t('triageTemplates.contentLabel')}
          </label>
          <textarea
            id="triage-template-content"
            value={content}
            onChange={(event) =>
              setEdits((current) => ({ ...current, [selectedType]: event.target.value }))
            }
            readOnly={!canManage}
            rows={16}
            maxLength={8000}
            className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 font-mono text-sm text-slate-900 shadow-sm outline-none focus:border-sky-500 focus:ring-2 focus:ring-sky-100 read-only:bg-slate-50"
          />
          <p className="text-xs text-slate-500">{t('triageTemplates.placeholderHelp')}</p>

          {canManage ? (
            <div className="flex flex-wrap gap-2">
              <button type="button" onClick={handleSave} disabled={isSaving || !content.trim()} className="rounded-md bg-sky-700 px-4 py-2 text-sm font-semibold text-white hover:bg-sky-800 disabled:cursor-not-allowed disabled:opacity-50">
                {isSaving ? t('triageTemplates.saving') : t('triageTemplates.saveDraft')}
              </button>
              <button type="button" onClick={handlePreview} disabled={isPreviewing || !content.trim()} className="rounded-md border border-slate-300 px-4 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50">
                {isPreviewing ? t('triageTemplates.previewing') : t('triageTemplates.preview')}
              </button>
              <button type="button" onClick={handlePublish} disabled={isPublishing || !draft} className="rounded-md bg-emerald-700 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-800 disabled:cursor-not-allowed disabled:opacity-50">
                {isPublishing ? t('triageTemplates.publishing') : t('triageTemplates.publish')}
              </button>
            </div>
          ) : (
            <p className="rounded-md bg-slate-100 px-3 py-2 text-sm text-slate-600">{t('triageTemplates.readOnly')}</p>
          )}

          {feedback && <p role="status" className="text-sm text-slate-700">{feedback}</p>}

          {previewContent && (
            <div className="rounded-md border border-sky-200 bg-sky-50 p-4">
              <h3 className="text-sm font-semibold text-sky-950">{t('triageTemplates.previewTitle')}</h3>
              <p className="mt-2 whitespace-pre-wrap text-sm text-slate-800">{previewContent}</p>
            </div>
          )}
        </div>

        <TemplateHistory versions={versions} />
      </div>
    </div>
  )
}

function TemplateHistory({ versions }: { versions: TriagemMessageTemplate[] }) {
  const { t, i18n } = useTranslation()

  return (
    <aside>
      <h3 className="text-sm font-semibold text-slate-900">{t('triageTemplates.historyTitle')}</h3>
      <div className="mt-3 space-y-2">
        {versions.map((version) => (
          <div key={version.id} className="rounded-md border border-slate-200 p-3 text-sm">
            <div className="flex items-center justify-between gap-2">
              <span className="font-semibold text-slate-900">v{version.version}</span>
              <span className="text-xs text-slate-500">
                {t(`triageTemplates.status.${version.status}`)}
              </span>
            </div>
            <p className="mt-1 text-xs text-slate-500">
              {new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }).format(
                new Date(version.publishedAtUtc ?? version.updatedAtUtc),
              )}
            </p>
          </div>
        ))}
      </div>
    </aside>
  )
}
