import type { ApexOptions } from 'apexcharts'
import ReactApexChart from 'react-apexcharts'
import { useTranslation } from 'react-i18next'
import { Card } from '../../../shared/components/ui/Card'
import type { AiUsageDailyPoint } from '../../../shared/types/aiUsage'

interface AiUsageDailyChartProps {
  daily: AiUsageDailyPoint[]
}

/** Formata `YYYY-MM-DD` como data curta no locale ativo (eixo X do gráfico). */
function formatDay(date: string, locale: string): string {
  const [year, month, day] = date.split('-').map(Number)
  return new Date(year, (month || 1) - 1, day || 1).toLocaleDateString(locale, { day: '2-digit', month: 'short' })
}

/**
 * Série diária de chamadas e custo estimado (`GET /api/llm-usage/summary`,
 * campo `daily`). Gráfico de linha com dois eixos (ApexCharts, já usado em
 * outros pontos do Office — RevenueTrendChart/KpiCard).
 */
export function AiUsageDailyChart({ daily }: AiUsageDailyChartProps) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'pt-BR'

  const options: ApexOptions = {
    chart: { type: 'line', toolbar: { show: false }, fontFamily: 'var(--font-sans)' },
    colors: ['var(--color-atua-blue)', 'var(--color-status-success)'],
    dataLabels: { enabled: false },
    stroke: { curve: 'smooth', width: 2 },
    grid: { borderColor: '#e2e8f0', strokeDashArray: 4 },
    legend: { show: true, position: 'top', horizontalAlign: 'right', fontSize: '12px' },
    xaxis: {
      categories: daily.map((point) => formatDay(point.date, locale)),
      axisBorder: { show: false },
      axisTicks: { show: false },
    },
    yaxis: [
      { seriesName: t('aiUsage.daily.calls'), labels: { style: { fontSize: '12px' } } },
      { seriesName: t('aiUsage.daily.estimatedCost'), opposite: true, labels: { style: { fontSize: '12px' } } },
    ],
    tooltip: { shared: true, intersect: false },
  }

  const series = [
    { name: t('aiUsage.daily.calls'), data: daily.map((point) => point.calls) },
    { name: t('aiUsage.daily.estimatedCost'), data: daily.map((point) => point.estimatedCost) },
  ]

  return (
    <Card>
      <Card.Header>
        <h2 className="text-sm font-semibold text-slate-700">{t('aiUsage.daily.title')}</h2>
      </Card.Header>
      <Card.Body>
        {daily.length > 0 ? (
          <ReactApexChart options={options} series={series} type="line" height={280} />
        ) : (
          <p className="text-sm text-slate-500">{t('aiUsage.daily.empty')}</p>
        )}
      </Card.Body>
    </Card>
  )
}
