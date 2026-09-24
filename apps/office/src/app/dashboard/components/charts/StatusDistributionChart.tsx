import type { ApexOptions } from 'apexcharts'
import ReactApexChart from 'react-apexcharts'
import type { WorkOrderStatusDistributionItem } from '../../hooks/useWorkOrderMetricsSummary'
import { getProviderStatusTone, type StatusTone } from '../../lib/providerStatus'

const TONE_COLOR_VARS: Record<StatusTone, string> = {
  success: 'var(--color-status-success)',
  danger: 'var(--color-status-danger)',
  info: 'var(--color-status-info)',
  warning: 'var(--color-status-warning)',
  neutral: 'var(--color-status-neutral)',
}

interface StatusDistributionChartProps {
  /** Distribuição de OS por status atual. */
  distribution: WorkOrderStatusDistributionItem[]
  /** Traduz um status cru para exibição no eixo. */
  translateStatus: (status: string) => string
}

/**
 * Gráfico de barras horizontais para comparar a distribuição de OS por
 * status (diretriz de dashboard: barras em vez de pizza/donut para
 * comparação). Horizontal evita corte/rotação de rótulos longos. Cores vêm
 * dos tokens de status centralizados. Espera-se uma distribuição já
 * reduzida aos status mais relevantes (ver `aggregateTopStatuses`).
 */
export function StatusDistributionChart({ distribution, translateStatus }: StatusDistributionChartProps) {
  const colors = distribution.map((item) => TONE_COLOR_VARS[getProviderStatusTone(item.status)])

  const options: ApexOptions = {
    chart: { type: 'bar', toolbar: { show: false }, fontFamily: 'var(--font-sans)' },
    colors,
    plotOptions: {
      bar: { distributed: true, borderRadius: 4, horizontal: true, barHeight: '65%' },
    },
    dataLabels: {
      enabled: true,
      style: { fontSize: '11px', colors: ['#334155'] },
      offsetX: 8,
      dropShadow: { enabled: false },
    },
    legend: { show: false },
    grid: { borderColor: '#e2e8f0', strokeDashArray: 4 },
    xaxis: {
      categories: distribution.map((item) => translateStatus(item.status)),
      axisBorder: { show: false },
      axisTicks: { show: false },
      labels: { style: { fontSize: '11px' } },
    },
    yaxis: { labels: { style: { fontSize: '12px' } } },
    tooltip: { intersect: false },
  }

  const series = [{ name: 'total', data: distribution.map((item) => item.total) }]

  return <ReactApexChart options={options} series={series} type="bar" height={280} />
}
