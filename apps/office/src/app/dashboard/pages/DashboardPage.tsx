import { useMemo, useState } from 'react'
import { ServiceOrdersTable } from '../components/ServiceOrdersTable'
import { StatusSummary } from '../components/StatusSummary'
import { RevenueTrendChart } from '../components/charts/RevenueTrendChart'
import { StatusDistributionChart } from '../components/charts/StatusDistributionChart'
import { useMyTenants } from '../../office/hooks/useTenants'
import { useWorkOrderMetricsSummary } from '../hooks/useWorkOrderMetricsSummary'
import { useTranslation } from 'react-i18next'
import { Card } from '../../../shared/components/ui/Card'
import { KpiCard } from '../../../shared/components/ui/KpiCard'
import { translateProviderStatus } from '../lib/providerStatus'
import { aggregateTopStatuses, OTHER_STATUS_KEY } from '../lib/statusDistribution'

/** Status inicial exibido na tabela de OS ao carregar o Dashboard (RN do card padrão). */
const DEFAULT_STATUS = 'assigned'

/** Formata "AAAA-MM" para um rótulo curto de mês no locale ativo. */
function formatMonthLabel(month: string, locale: string): string {
  const [year, monthIndex] = month.split('-').map(Number)
  const date = new Date(year, (monthIndex || 1) - 1, 1)
  return date.toLocaleDateString(locale, { month: 'short', year: '2-digit' })
}

/**
 * Variação percentual entre dois valores, formatada com sinal (ex.: "+12%").
 * Quando não há base de comparação (`previous === 0`), evita o "+100%"
 * enganoso e sinaliza explicitamente que não há período anterior.
 */
function formatDelta(
  current: number,
  previous: number,
  newLabel: string,
): { delta: string; trend: 'up' | 'down' } {
  if (previous === 0) {
    return current > 0 ? { delta: newLabel, trend: 'up' } : { delta: '—', trend: 'up' }
  }
  const variation = Math.round(((current - previous) / previous) * 100)
  return { delta: `${variation >= 0 ? '+' : ''}${variation}%`, trend: variation >= 0 ? 'up' : 'down' }
}

/**
 * Dashboard: home pós-login do Office.
 *
 * O tenant ativo é resolvido do mesmo jeito que em SettingsPage.tsx
 * (useMyTenants().defaultTenantId): o Dashboard não implementa seleção
 * explícita de tenant — se o usuário tiver múltiplos tenants e nenhum
 * default, os cards/tabela simplesmente não disparam requisição
 * (tenantId null) até que a seleção seja resolvida em outro ponto do app.
 */
export function DashboardPage() {
  const { t, i18n } = useTranslation()
  const { defaultTenantId, isLoading, isError } = useMyTenants()
  const [selectedStatus, setSelectedStatus] = useState(DEFAULT_STATUS)
  const locale = i18n.resolvedLanguage ?? 'pt-BR'

  const { data: metrics, isLoading: isMetricsLoading, isError: isMetricsError } = useWorkOrderMetricsSummary(
    defaultTenantId,
  )

  const kpis = useMemo(() => {
    const trend = metrics?.trend ?? []
    if (trend.length === 0) return []

    const current = trend[trend.length - 1]
    const previous = trend[trend.length - 2] ?? { created: 0, completed: 0 }

    const newLabel = t('dashboard.kpi.new')
    const createdDelta = formatDelta(current.created, previous.created, newLabel)
    const completedDelta = formatDelta(current.completed, previous.completed, newLabel)

    const currentRate = current.created > 0 ? Math.round((current.completed / current.created) * 100) : 0
    const previousRate = previous.created > 0 ? Math.round((previous.completed / previous.created) * 100) : 0
    const rateDelta = formatDelta(currentRate, previousRate, newLabel)

    const totalActive = (metrics?.statusDistribution ?? []).reduce((sum, item) => sum + item.total, 0)

    return [
      {
        label: t('dashboard.kpi.created.label'),
        value: current.created,
        delta: createdDelta.delta,
        trend: createdDelta.trend,
        comparisonPeriod: t('dashboard.kpi.comparisonPeriod'),
        sparklineData: trend.slice(-6).map((point) => point.created),
      },
      {
        label: t('dashboard.kpi.completed.label'),
        value: current.completed,
        delta: completedDelta.delta,
        trend: completedDelta.trend,
        comparisonPeriod: t('dashboard.kpi.comparisonPeriod'),
        sparklineData: trend.slice(-6).map((point) => point.completed),
      },
      {
        label: t('dashboard.kpi.completionRate.label'),
        value: `${currentRate}%`,
        delta: rateDelta.delta,
        trend: rateDelta.trend,
        comparisonPeriod: t('dashboard.kpi.comparisonPeriod'),
      },
      {
        label: t('dashboard.kpi.totalActive.label'),
        value: totalActive,
      },
    ]
  }, [metrics, t])

  /**
   * Janela adaptativa para o gráfico de tendência: evita meses achatados em
   * zero antes do início real da operação (o eixo fixo de 12 meses dilui a
   * comparação entre "criadas" e "concluídas"). Cai para os últimos 6 meses
   * quando não há um trecho inicial sem atividade.
   */
  const trendChartWindow = useMemo(() => {
    const trend = metrics?.trend ?? []
    const firstActiveIndex = trend.findIndex((point) => point.created > 0 || point.completed > 0)
    if (firstActiveIndex <= 0) return trend.slice(-6)
    const windowStart = Math.max(0, firstActiveIndex - 1)
    return trend.slice(windowStart)
  }, [metrics])

  return (
    <div className="grid w-full grid-cols-12 gap-4">
      <div className="col-span-12 border-b border-slate-200 pb-5">
        <h1 className="text-2xl font-semibold text-slate-950 sm:text-3xl">{t('dashboard.title')}</h1>
        <p className="mt-1 max-w-2xl text-sm text-slate-600">
          {t('dashboard.subtitle')}
        </p>
      </div>

      {isLoading && (
        <p className="col-span-12 rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          {t('dashboard.loadingCompany')}
        </p>
      )}

      {!isLoading && isError && (
        <p
          role="alert"
          className="col-span-12 rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm"
        >
          {t('dashboard.companyError')}
        </p>
      )}

      {!isLoading && !isError && (
        <>
          {kpis.length > 0 && (
            <div className="col-span-12 grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
              {kpis.map((kpi) => (
                <KpiCard key={kpi.label} {...kpi} />
              ))}
            </div>
          )}

          {isMetricsLoading && (
            <p className="col-span-12 rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
              {t('dashboard.metrics.loading')}
            </p>
          )}

          {!isMetricsLoading && isMetricsError && (
            <p
              role="alert"
              className="col-span-12 rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm"
            >
              {t('dashboard.metrics.error')}
            </p>
          )}

          {!isMetricsLoading && !isMetricsError && metrics && (
            <>
              <div className="col-span-12 xl:col-span-8">
                <Card>
                  <Card.Header>
                    <h2 className="text-sm font-semibold text-slate-700">{t('dashboard.metrics.trendTitle')}</h2>
                  </Card.Header>
                  <Card.Body>
                    <RevenueTrendChart
                      trend={trendChartWindow}
                      labels={{
                        created: t('dashboard.kpi.created.label'),
                        completed: t('dashboard.kpi.completed.label'),
                      }}
                      formatMonth={(month) => formatMonthLabel(month, locale)}
                    />
                  </Card.Body>
                </Card>
              </div>

              <div className="col-span-12 xl:col-span-4">
                <Card>
                  <Card.Header>
                    <h2 className="text-sm font-semibold text-slate-700">{t('dashboard.metrics.distributionTitle')}</h2>
                  </Card.Header>
                  <Card.Body>
                    <StatusDistributionChart
                      distribution={aggregateTopStatuses(metrics.statusDistribution)}
                      translateStatus={(status) =>
                        status === OTHER_STATUS_KEY ? t('dashboard.metrics.otherStatus') : translateProviderStatus(status, t)
                      }
                    />
                  </Card.Body>
                </Card>
              </div>
            </>
          )}

          <div className="col-span-12">
            <StatusSummary tenantId={defaultTenantId} selectedStatus={selectedStatus} onSelectStatus={setSelectedStatus} />
          </div>
          <div className="col-span-12">
            <ServiceOrdersTable tenantId={defaultTenantId} status={selectedStatus} />
          </div>
        </>
      )}
    </div>
  )
}
