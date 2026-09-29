import { useTranslation } from 'react-i18next'
import { Card } from '../../../../shared/components/ui/Card'
import { useWorkOrderSubStatusSummary } from '../../hooks/useWorkOrderSubStatusSummary'
import { NO_SUBSTATUS_LABEL, SubStatusSummaryChart } from './SubStatusSummaryChart'

interface SubStatusSummaryProps {
  /** Tenant ativo do usuário (ver useMyTenants().defaultTenantId). */
  tenantId: string | null
}

/**
 * Seção "OS por substatus" do Dashboard (RF-032): card com um gráfico de
 * barras do total de OS Em Aberto agrupado por substatus, colorido por
 * responsável (RF-031.2).
 *
 * O grupo "Sem substatus" (OS sem substatus mapeado) não entra no gráfico
 * — misturado às barras com substatus reais ele não indica nenhuma ação
 * possível — e é exibido à parte como uma nota informativa.
 */
export function SubStatusSummary({ tenantId }: SubStatusSummaryProps) {
  const { t } = useTranslation()
  const { groups, isLoading, isError } = useWorkOrderSubStatusSummary(tenantId)

  const mappedGroups = groups.filter((group) => group.subStatus !== NO_SUBSTATUS_LABEL)
  const mappedTotal = mappedGroups.reduce((sum, group) => sum + group.total, 0)
  const unmappedTotal = groups
    .filter((group) => group.subStatus === NO_SUBSTATUS_LABEL)
    .reduce((sum, group) => sum + group.total, 0)

  return (
    <section aria-labelledby="substatus-summary-heading" className="space-y-3">
      <h2 id="substatus-summary-heading" className="text-sm font-semibold text-slate-700">
        {t('dashboard.subStatus.title')}
      </h2>

      {isLoading && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('dashboard.subStatus.loading')}
        </p>
      )}

      {!isLoading && isError && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          {t('dashboard.subStatus.error')}
        </p>
      )}

      {!isLoading && !isError && groups.length === 0 && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('dashboard.subStatus.empty')}
        </p>
      )}

      {!isLoading && !isError && groups.length > 0 && (
        <>
          {mappedGroups.length > 0 && (
            <Card>
              <Card.Body>
                <div className="mb-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-1 text-xs text-slate-500">
                  <span>{t('dashboard.subStatus.totalNote', { count: mappedTotal })}</span>
                  {unmappedTotal > 0 && <span>{t('dashboard.subStatus.unmappedNote', { count: unmappedTotal })}</span>}
                </div>
                <SubStatusSummaryChart groups={mappedGroups} />
              </Card.Body>
            </Card>
          )}

          {mappedGroups.length === 0 && unmappedTotal > 0 && (
            <p className="rounded-md border border-slate-200 bg-slate-50 px-4 py-3 text-xs text-slate-500">
              {t('dashboard.subStatus.unmappedNote', { count: unmappedTotal })}
            </p>
          )}
        </>
      )}
    </section>
  )
}
