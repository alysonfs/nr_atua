import { useTranslation } from 'react-i18next'
import { Badge } from '../../../shared/components/ui/Badge'
import type { TriagemWarrantyEstimate } from '../../../shared/types/triagePool'
import {
  formatWarrantyPeriodLabel,
  translateWarrantyEstimateLabel,
  warrantyEstimateTone,
} from '../lib/warrantyEstimate'

function formatIssuedAt(isoDate: string | null | undefined, locale: string): string | null {
  if (!isoDate) return null
  const date = new Date(isoDate)
  if (Number.isNaN(date.getTime())) return null
  return date.toLocaleDateString(locale, { day: '2-digit', month: '2-digit', year: 'numeric' })
}

interface InvoiceSectionProps {
  notaFiscalNumero: string | null | undefined
  notaFiscalSerie: string | null | undefined
  warrantyEstimate: TriagemWarrantyEstimate | null | undefined
  locale: string
}

/**
 * Nota fiscal + selo de garantia (estimativa), exibido no detalhe da
 * triagem (C2) e reaproveitado no card de triagem da OS. Número, série e
 * data de emissão são sempre exibidos (com "—" quando ausentes). A data de
 * emissão é obtida de `warrantyEstimate.baseDate` (o backend não publica um
 * campo de emissão separado). O selo é sempre uma *estimativa* com base no
 * prazo padrão (hoje 12 meses) — nunca uma certeza jurídica; a regra por
 * categoria de produto é um trabalho futuro (ver nota discreta abaixo do
 * selo).
 */
export function InvoiceSection({
  notaFiscalNumero,
  notaFiscalSerie,
  warrantyEstimate,
  locale,
}: InvoiceSectionProps) {
  const { t } = useTranslation()
  const notInformed = t('workOrderDetail.notInformed')
  const issuedAtLabel = formatIssuedAt(warrantyEstimate?.baseDate, locale)

  return (
    <section className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
      <h2 className="mb-3 text-sm font-semibold text-slate-700">{t('triagePoolDetail.invoice.title')}</h2>
      <dl className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <div>
          <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">
            {t('triagePoolDetail.invoice.number')}
          </dt>
          <dd className="mt-1 text-sm text-slate-800">{notaFiscalNumero ?? notInformed}</dd>
        </div>
        <div>
          <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">
            {t('triagePoolDetail.invoice.series')}
          </dt>
          <dd className="mt-1 text-sm text-slate-800">{notaFiscalSerie ?? notInformed}</dd>
        </div>
        <div>
          <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">
            {t('triagePoolDetail.invoice.issuedAt')}
          </dt>
          <dd className="mt-1 text-sm text-slate-800">{issuedAtLabel ?? notInformed}</dd>
        </div>
      </dl>
      <div className="mt-3">
        <Badge tone={warrantyEstimateTone(warrantyEstimate?.status)}>
          {translateWarrantyEstimateLabel(t, warrantyEstimate)}
        </Badge>
      </div>
      <p className="mt-2 text-xs text-slate-400">
        {t('triagePoolDetail.invoice.warranty.note', {
          period: formatWarrantyPeriodLabel(t, warrantyEstimate?.defaultMonths),
        })}
      </p>
    </section>
  )
}
