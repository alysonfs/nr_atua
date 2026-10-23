import type { ReactNode } from 'react'
import { useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useMyTenants } from '../../office/hooks/useTenants'
import { useTriagePoolDetail } from '../hooks/useTriagePoolDetail'
import { ChecklistSection } from '../components/ChecklistSection'
import { AttachmentsSection } from '../components/AttachmentsSection'
import { ConversationSection } from '../components/ConversationSection'
import { Badge } from '../../../shared/components/ui/Badge'
import { translateTriagemPoolStatus, triagemPoolStatusTone } from '../lib/triageStatus'

function Field({ label, value }: { label: string; value: string | null }) {
  const { t } = useTranslation()
  return (
    <div>
      <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</dt>
      <dd className="mt-1 text-sm text-slate-800">{value ?? t('workOrderDetail.notInformed')}</dd>
    </div>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
      <h2 className="mb-3 text-sm font-semibold text-slate-700">{title}</h2>
      <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">{children}</dl>
    </section>
  )
}

/**
 * Página `/triage-pool/:triagemId` (C2): detalhe somente leitura de uma
 * triagem — do pool ou ainda em triagem (pré-pool) — com dados da
 * OS/consumidor, motivo do estado, checklist, anexos e conversa
 * cronológica. O Office é ambiente de gestão: não assume a triagem nem
 * conversa com o consumidor.
 */
export function TriagePoolDetailPage() {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'pt-BR'
  const { triagemId } = useParams<{ triagemId: string }>()
  const { defaultTenantId, isLoading: isTenantLoading, isError: isTenantError } = useMyTenants()
  const { detail, isLoading, isError, notFound } = useTriagePoolDetail(
    defaultTenantId,
    triagemId ?? null,
  )

  if (isTenantLoading || isLoading) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-6 sm:px-6 lg:px-8">
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('triagePoolDetail.loading')}
        </p>
      </div>
    )
  }

  if (notFound) {
    return (
      <div className="mx-auto w-full max-w-5xl space-y-4 px-4 py-6 sm:px-6 lg:px-8">
        <p
          role="alert"
          className="rounded-md border-l-4 border-amber-500 bg-white p-4 text-sm text-amber-800 shadow-sm"
        >
          {t('triagePoolDetail.notFoundMessage')}
        </p>
      </div>
    )
  }

  if (isTenantError || isError || !detail) {
    return (
      <div className="mx-auto w-full max-w-5xl space-y-4 px-4 py-6 sm:px-6 lg:px-8">
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          {t('triagePoolDetail.loadError')}
        </p>
      </div>
    )
  }

  return (
    <div className="mx-auto w-full max-w-5xl space-y-5 px-4 py-6 sm:px-6 lg:px-8">
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-slate-200 pb-5">
        <div>
          <h1 className="text-2xl font-semibold text-slate-950 sm:text-3xl">
            {detail.workOrder.workOrderProviderNo ?? detail.workOrder.workOrderId}
          </h1>
          <div className="mt-2 flex items-center gap-2">
            <Badge tone={triagemPoolStatusTone(detail.status)}>{translateTriagemPoolStatus(t, detail.status)}</Badge>
          </div>
        </div>
      </div>

      <p className="text-xs text-slate-500">{t('triagePoolDetail.readOnlyNotice')}</p>

      {detail.stateReason && (
        <p className="rounded-md border border-slate-200 bg-slate-50 p-3 text-sm text-slate-700">
          <span className="font-semibold">{t('triagePoolDetail.stateReason')}: </span>
          {detail.stateReason}
        </p>
      )}

      {detail.assumidaPorUserId && (
        <p className="rounded-md border border-sky-200 bg-sky-50 p-3 text-sm text-sky-800">
          {t('triagePoolDetail.assumedBy', {
            user: detail.assumidaPorUserId,
            date: detail.assumidaEmUtc
              ? new Date(detail.assumidaEmUtc).toLocaleString(locale, {
                  day: '2-digit',
                  month: '2-digit',
                  year: 'numeric',
                  hour: '2-digit',
                  minute: '2-digit',
                })
              : '—',
          })}
        </p>
      )}

      <Section title={t('triagePoolDetail.workOrder.title')}>
        <Field label={t('triagePoolDetail.workOrder.providerNo')} value={detail.workOrder.workOrderProviderNo} />
        <Field label={t('triagePoolDetail.workOrder.customerName')} value={detail.workOrder.customerName} />
        <Field label={t('triagePoolDetail.workOrder.customerCpf')} value={detail.workOrder.customerCpf} />
        <Field label={t('triagePoolDetail.workOrder.contactPhone')} value={detail.workOrder.contactPhone} />
        <Field label={t('triagePoolDetail.workOrder.contactEmail')} value={detail.workOrder.contactEmail} />
        <Field label={t('triagePoolDetail.workOrder.address')} value={detail.workOrder.address} />
        <Field label={t('triagePoolDetail.workOrder.zipCode')} value={detail.workOrder.zipCode} />
        <Field label={t('triagePoolDetail.workOrder.city')} value={detail.workOrder.cityName} />
        <Field label={t('triagePoolDetail.workOrder.state')} value={detail.workOrder.stateName} />
        <Field label={t('triagePoolDetail.workOrder.productBrand')} value={detail.workOrder.productBrand} />
        <Field label={t('triagePoolDetail.workOrder.productModel')} value={detail.workOrder.productModel} />
        <Field label={t('triagePoolDetail.workOrder.symptom')} value={detail.workOrder.symptom} />
      </Section>

      <ChecklistSection checklist={detail.checklist} />
      <AttachmentsSection attachments={detail.attachments} />
      <ConversationSection conversation={detail.conversation} locale={locale} />
    </div>
  )
}
