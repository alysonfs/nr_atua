import type { ReactNode } from 'react'
import { useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useMyTenants } from '../../office/hooks/useTenants'
import { useWorkOrderDetail } from '../hooks/useWorkOrderDetail'
import { translateProviderStatus } from '../lib/providerStatus'

/** Formata uma data ISO 8601 no padrão do locale ativo (dd/mm/aaaa hh:mm). */
function formatDateTime(isoDate: string | null, locale: string): string {
  if (!isoDate) return '—'
  return new Date(isoDate).toLocaleString(locale, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function Field({ label, value, emptyText }: { label: string; value: string | null; emptyText: string }) {
  return (
    <div>
      <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</dt>
      <dd className="mt-1 text-sm text-slate-800">{value ?? emptyText}</dd>
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
 * RF-026.5/RF-026.6: página de detalhes de uma OS, exibindo campos
 * normalizados (cliente, localização, produto, atendimento) e a linha do
 * tempo de transições de status.
 *
 * O tenant ativo é resolvido do mesmo jeito que em DashboardPage.tsx
 * (useMyTenants().defaultTenantId).
 */
export function WorkOrderDetailPage() {
  const { t, i18n } = useTranslation()
  const { workOrderId } = useParams<{ workOrderId: string }>()
  const { defaultTenantId, isLoading: isTenantLoading, isError: isTenantError } = useMyTenants()
  const { detail, isLoading, isError, notFound } = useWorkOrderDetail(defaultTenantId, workOrderId ?? null)
  const locale = i18n.resolvedLanguage ?? 'pt-BR'
  const notInformed = t('workOrderDetail.notInformed')

  if (isTenantLoading || isLoading) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-6 sm:px-6 lg:px-8">
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('workOrderDetail.loading')}
        </p>
      </div>
    )
  }

  if (notFound) {
    return (
      <div className="mx-auto w-full max-w-5xl space-y-4 px-4 py-6 sm:px-6 lg:px-8">
        <p role="alert" className="rounded-md border-l-4 border-amber-500 bg-white p-4 text-sm text-amber-800 shadow-sm">
          {t('workOrderDetail.notFoundMessage')}
        </p>
      </div>
    )
  }

  if (isTenantError || isError || !detail) {
    return (
      <div className="mx-auto w-full max-w-5xl space-y-4 px-4 py-6 sm:px-6 lg:px-8">
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          {t('workOrderDetail.loadError')}
        </p>
      </div>
    )
  }

  return (
    <div className="mx-auto w-full max-w-5xl space-y-5 px-4 py-6 sm:px-6 lg:px-8">
      <div className="border-b border-slate-200 pb-5">
        <h1 className="text-2xl font-semibold text-slate-950 sm:text-3xl">
          {detail.workOrderProviderNo ?? detail.workOrderProviderId}
        </h1>
      </div>

      <Section title={t('workOrderDetail.generalInfo.title')}>
        <Field label={t('workOrderDetail.generalInfo.id')} value={detail.workOrderProviderId} emptyText={notInformed} />
        <Field label={t('workOrderDetail.generalInfo.providerNo')} value={detail.workOrderProviderNo} emptyText={notInformed} />
        <Field label={t('workOrderDetail.generalInfo.status')} value={translateProviderStatus(detail.status, t)} emptyText={notInformed} />
        <Field label={t('workOrderDetail.generalInfo.createdAt')} value={formatDateTime(detail.createdAt, locale)} emptyText={notInformed} />
        <Field label={t('workOrderDetail.generalInfo.updatedAt')} value={formatDateTime(detail.updatedAt, locale)} emptyText={notInformed} />
        <Field label={t('workOrderDetail.generalInfo.providerCreatedAt')} value={formatDateTime(detail.providerCreatedAt, locale)} emptyText={notInformed} />
        <Field label={t('workOrderDetail.generalInfo.providerUpdatedAt')} value={formatDateTime(detail.providerUpdatedAt, locale)} emptyText={notInformed} />
      </Section>

      <Section title={t('workOrderDetail.customer.title')}>
        <Field label={t('workOrderDetail.customer.type')} value={detail.customerType} emptyText={notInformed} />
        <Field label={t('workOrderDetail.customer.name')} value={detail.customerName} emptyText={notInformed} />
        <Field label={t('workOrderDetail.customer.cpf')} value={detail.customerCpf} emptyText={notInformed} />
        <Field label={t('workOrderDetail.customer.email')} value={detail.contactEmail} emptyText={notInformed} />
        <Field label={t('workOrderDetail.customer.phone')} value={detail.contactPhone} emptyText={notInformed} />
        <Field label={t('workOrderDetail.customer.contactName')} value={detail.contactName} emptyText={notInformed} />
      </Section>

      <Section title={t('workOrderDetail.location.title')}>
        <Field label={t('workOrderDetail.location.address')} value={detail.address} emptyText={notInformed} />
        <Field label={t('workOrderDetail.location.zipCode')} value={detail.zipCode} emptyText={notInformed} />
        <Field label={t('workOrderDetail.location.country')} value={detail.countryName} emptyText={notInformed} />
        <Field label={t('workOrderDetail.location.state')} value={detail.stateName} emptyText={notInformed} />
        <Field label={t('workOrderDetail.location.city')} value={detail.cityName} emptyText={notInformed} />
      </Section>

      <Section title={t('workOrderDetail.product.title')}>
        <Field label={t('workOrderDetail.product.brand')} value={detail.productBrand} emptyText={notInformed} />
        <Field label={t('workOrderDetail.product.code')} value={detail.productCode ?? detail.pdCode} emptyText={notInformed} />
        <Field label={t('workOrderDetail.product.category')} value={detail.productCategoryCode} emptyText={notInformed} />
        <Field label={t('workOrderDetail.product.model')} value={detail.productModel} emptyText={notInformed} />
        <Field label={t('workOrderDetail.product.status')} value={detail.productStatus} emptyText={notInformed} />
      </Section>

      <Section title={t('workOrderDetail.service.title')}>
        <Field label={t('workOrderDetail.service.symptom')} value={detail.symptom} emptyText={notInformed} />
        <Field label={t('workOrderDetail.service.serviceRequestId')} value={detail.serviceRequestId} emptyText={notInformed} />
        <Field
          label={t('workOrderDetail.service.amount')}
          value={detail.amount !== null ? detail.amount.toLocaleString(locale) : null}
          emptyText={notInformed}
        />
      </Section>

      <section className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="mb-3 text-sm font-semibold text-slate-700">{t('workOrderDetail.history.title')}</h2>
        {detail.history.length === 0 ? (
          <p className="text-sm text-slate-500">{t('workOrderDetail.history.empty')}</p>
        ) : (
          <ol className="space-y-2 border-l-2 border-slate-200 pl-4">
            {detail.history.map((entry, index) => (
              <li key={`${entry.status}-${entry.createdAt}-${index}`} className="text-sm text-slate-700">
                <span className="font-medium text-slate-900">{translateProviderStatus(entry.status, t)}</span>
                {' — '}
                <span className="text-slate-500">{formatDateTime(entry.createdAt, locale)}</span>
              </li>
            ))}
          </ol>
        )}
      </section>
    </div>
  )
}
