import type { ApexOptions } from 'apexcharts'
import ReactApexChart from 'react-apexcharts'
import type { WorkOrderTrendPoint } from '../../hooks/useWorkOrderMetricsSummary'

interface RevenueTrendChartProps {
  /** Série mensal de OS criadas x concluídas. */
  trend: WorkOrderTrendPoint[]
  /** Rótulos das séries (i18n). */
  labels: { created: string; completed: string }
  /** Formata o mês ("AAAA-MM") para exibição no eixo X. */
  formatMonth: (month: string) => string
}

/**
 * Gráfico de área/linha comparando OS criadas x concluídas ao longo do
 * tempo. Cor primária = token `atua-blue` (não a paleta do Duralux).
 */
export function RevenueTrendChart({ trend, labels, formatMonth }: RevenueTrendChartProps) {
  const options: ApexOptions = {
    chart: { type: 'area', toolbar: { show: false }, fontFamily: 'var(--font-sans)' },
    colors: ['var(--color-atua-blue)', 'var(--color-status-success)'],
    dataLabels: { enabled: false },
    stroke: { curve: 'smooth', width: 2 },
    fill: {
      type: 'gradient',
      gradient: { shadeIntensity: 1, opacityFrom: 0.35, opacityTo: 0.05, stops: [0, 90, 100] },
    },
    grid: { borderColor: '#e2e8f0', strokeDashArray: 4 },
    legend: { show: true, position: 'top', horizontalAlign: 'right', fontSize: '12px' },
    xaxis: {
      categories: trend.map((point) => formatMonth(point.month)),
      axisBorder: { show: false },
      axisTicks: { show: false },
    },
    yaxis: { labels: { style: { fontSize: '12px' } } },
    tooltip: { shared: true, intersect: false },
  }

  const series = [
    { name: labels.created, data: trend.map((point) => point.created) },
    { name: labels.completed, data: trend.map((point) => point.completed) },
  ]

  return <ReactApexChart options={options} series={series} type="area" height={280} />
}
