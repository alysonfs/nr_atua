import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Badge } from '../../../shared/components/ui/Badge'
import { formatTriageDurationLabel } from '../../triage-pool/lib/formatTriageDuration'
import { formatAiUsageDetailLabel } from '../../triage-pool/lib/formatAiUsage'
import {
  formatWarrantyPeriodLabel,
  translateWarrantyEstimateLabel,
  warrantyEstimateTone,
} from '../../triage-pool/lib/warrantyEstimate'
import type { TriagemLlmUsage, TriagemWarrantyEstimate } from '../../../shared/types/triagePool'

function formatIssuedAt(isoDate: string | null | undefined, locale: string): string | null {
  if (!isoDate) return null
  const date = new Date(isoDate)
  if (Number.isNaN(date.getTime())) return null
  return date.toLocaleDateString(locale, { day: '2-digit', month: '2-digit', year: 'numeric' })
}

interface TriageSummaryCardProps {
  triagemId: string
  startedAtUtc: string | null | undefined
  finishedAtUtc: string | null | undefined
  llmUsage: TriagemLlmUsage | null | undefined
  notaFiscalNumero: string | null | undefined
  notaFiscalSerie: string | null | undefined
  warrantyEstimate: TriagemWarrantyEstimate | null | undefined
  locale: string
}

/**
 * Card discreto "Triagem" no detalhe da OS: tempo de triagem, custo de IA,
 * dados da nota fiscal e selo de garantia (estimativa), além do link já
 * existente "Ver triagem". Todos os campos são opcionais enquanto o
 * backend não os publica — exibidos como "—" nesse caso.
 */
export function TriageSummaryCard({
  triagemId,
  startedAtUtc,
  finishedAtUtc,
  llmUsage,
  notaFiscalNumero,
  notaFiscalSerie,
  warrantyEstimate,
  locale,
}: TriageSummaryCardProps) {
  const { t } = useTranslation()
  const notInformed = t('workOrderDetail.notInformed')
  const issuedAtLabel = formatIssuedAt(warrantyEstimate?.baseDate, locale)

  return (
    <section className="rounded-lg border border-slate-200 bg-slate-50 p-4 shadow-sm">
      <div className="mb-3 flex items-center justify-between gap-2">
        <h2 className="text-sm font-semibold text-slate-700">{t('workOrderDetail.triage.title')}</h2>
        <Link to={`/triage-pool/${triagemId}`} className="text-sm font-medium text-sky-700 hover:underline">
          {t('workOrderDetail.viewTriage')}
        </Link>
      </div>

      <dl className="grid grid-cols-1 gap-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <div>
          <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">
            {t('workOrderDetail.triage.duration')}
          </dt>
          <dd className="mt-1 text-slate-800">{formatTriageDurationLabel(t, startedAtUtc, finishedAtUtc)}</dd>
        </div>
        <div>
          <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">
            {t('workOrderDetail.triage.aiCost')}
          </dt>
          <dd className="mt-1 text-slate-800">{formatAiUsageDetailLabel(t, llmUsage, locale)}</dd>
        </div>
        <div>
          <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">
            {t('workOrderDetail.triage.invoice')}
          </dt>
          <dd className="mt-1 text-slate-800">
            {notaFiscalNumero || notaFiscalSerie || issuedAtLabel ? (
              <>
                {notaFiscalNumero ?? notInformed}
                {notaFiscalSerie && ` / ${t('triagePoolDetail.invoice.series')} ${notaFiscalSerie}`}
                {issuedAtLabel && <span className="block text-xs text-slate-500">{issuedAtLabel}</span>}
              </>
            ) : (
              notInformed
            )}
          </dd>
        </div>
        <div>
          <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">
            {t('workOrderDetail.triage.warranty')}
          </dt>
          <dd className="mt-1">
            <Badge tone={warrantyEstimateTone(warrantyEstimate?.status)}>
              {translateWarrantyEstimateLabel(t, warrantyEstimate)}
            </Badge>
          </dd>
        </div>
      </dl>
      <p className="mt-3 text-xs text-slate-400">
        {t('triagePoolDetail.invoice.warranty.note', {
          period: formatWarrantyPeriodLabel(t, warrantyEstimate?.defaultMonths),
        })}
      </p>
    </section>
  )
}
