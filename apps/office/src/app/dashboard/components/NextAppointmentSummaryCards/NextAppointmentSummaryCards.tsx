import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useWorkOrderNextAppointmentSummary } from '../../hooks/useWorkOrderNextAppointmentSummary'

interface NextAppointmentSummaryCardsProps {
  /** Tenant ativo do usuário (ver useMyTenants().defaultTenantId). */
  tenantId: string | null
}

type NextAppointmentFilter = 'overdue' | 'due-today' | 'due-tomorrow'
type NextAppointmentTone = 'danger' | 'warning' | 'info'

interface NextAppointmentCardProps {
  filter: NextAppointmentFilter
  total: number
  label: string
  tone: NextAppointmentTone
}

const TONE_BORDER_CLASSES: Record<NextAppointmentTone, string> = {
  danger: 'border-l-status-danger',
  warning: 'border-l-status-warning',
  info: 'border-l-status-info',
}

/**
 * Card individual de prazo de atendimento: apenas navega (drill-down) para
 * `/service-orders` com o filtro de prazo pré-aplicado (RF-030.5).
 */
function NextAppointmentCard({ filter, total, label, tone }: NextAppointmentCardProps) {
  return (
    <Link
      to={`/service-orders?nextAppointmentFilter=${filter}`}
      className={`flex flex-col gap-2 rounded-lg border border-l-4 ${TONE_BORDER_CLASSES[tone]} border-slate-200 bg-white p-4 shadow-sm transition-colors hover:bg-slate-50`}
    >
      <span className="text-sm font-medium text-slate-600">{label}</span>
      <p className="text-2xl font-bold text-slate-950">{total}</p>
    </Link>
  )
}

/**
 * Seção "prazo de atendimento" do Dashboard (RF-030): 3 cards com a
 * contagem de OS Em Aberto vencidas, vencendo hoje, ou em atenção (vence
 * amanhã). Cada card navega para `/service-orders` já filtrado pelo prazo
 * clicado.
 */
export function NextAppointmentSummaryCards({ tenantId }: NextAppointmentSummaryCardsProps) {
  const { t } = useTranslation()
  const { summary, isLoading, isError } = useWorkOrderNextAppointmentSummary(tenantId)

  return (
    <section aria-labelledby="next-appointment-summary-heading" className="space-y-3">
      <h2 id="next-appointment-summary-heading" className="text-sm font-semibold text-slate-700">
        {t('dashboard.nextAppointment.title')}
      </h2>

      {isLoading && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('dashboard.nextAppointment.loading')}
        </p>
      )}

      {!isLoading && isError && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          {t('dashboard.nextAppointment.error')}
        </p>
      )}

      {!isLoading && !isError && summary && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <NextAppointmentCard
            filter="overdue"
            total={summary.overdue}
            label={t('dashboard.nextAppointment.overdue')}
            tone="danger"
          />
          <NextAppointmentCard
            filter="due-today"
            total={summary.dueToday}
            label={t('dashboard.nextAppointment.dueToday')}
            tone="warning"
          />
          <NextAppointmentCard
            filter="due-tomorrow"
            total={summary.dueTomorrow}
            label={t('dashboard.nextAppointment.dueTomorrow')}
            tone="info"
          />
        </div>
      )}
    </section>
  )
}
