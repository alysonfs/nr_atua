import Chart from 'react-apexcharts'
import type { ApexOptions } from 'apexcharts'

interface SparklineProps {
  /** Valores diários usados para desenhar a linha do gráfico. */
  data: number[]
  /** Cor da linha (classe Tailwind não se aplica aqui; usar valor hex/CSS). */
  color: string
  /** Rótulo acessível descrevendo o gráfico. */
  ariaLabel: string
}

/**
 * Mini-gráfico de linha (sparkline) usado dentro dos cards de summary
 * month para exibir a evolução diária de um status ao longo do mês.
 */
export function Sparkline({ data, color, ariaLabel }: SparklineProps) {
  const options: ApexOptions = {
    chart: {
      type: 'line',
      sparkline: { enabled: true },
      animations: { enabled: false },
    },
    stroke: {
      curve: 'smooth',
      width: 2,
    },
    colors: [color],
    tooltip: {
      enabled: true,
      x: { show: false },
      y: {
        title: {
          formatter: () => '',
        },
      },
    },
  }

  const series = [{ name: ariaLabel, data }]

  return (
    <div role="img" aria-label={ariaLabel} data-testid="summary-month-sparkline" className="h-12 w-full">
      <Chart options={options} series={series} type="line" height={48} />
    </div>
  )
}
