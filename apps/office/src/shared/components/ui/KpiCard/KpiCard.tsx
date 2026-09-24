import type { ApexOptions } from 'apexcharts'
import ReactApexChart from 'react-apexcharts'
import { Card } from '../Card'
import { cn } from '../../../lib/cn'

export interface KpiCardProps {
  /** Rótulo curto do indicador (ex.: "OS criadas"). */
  label: string
  /** Valor principal exibido em destaque (já formatado, ex.: "128" ou "72%"). */
  value: string | number
  /** Variação percentual em relação ao período de comparação (ex.: "+12%"). */
  delta?: string
  /** Direção da variação, usada para colorir o delta (success/danger). */
  trend?: 'up' | 'down'
  /** Texto do período de comparação (ex.: "vs. mês anterior"). */
  comparisonPeriod?: string
  /** Série numérica opcional para o mini gráfico (sparkline). */
  sparklineData?: number[]
}

const SPARKLINE_OPTIONS: ApexOptions = {
  chart: { type: 'line', sparkline: { enabled: true }, toolbar: { show: false } },
  stroke: { curve: 'smooth', width: 2 },
  tooltip: { enabled: false },
  colors: ['var(--color-atua-blue)'],
}

/**
 * Card de KPI do design system: valor principal, variação (delta) frente a
 * um período de comparação e, opcionalmente, um mini gráfico (sparkline).
 */
export function KpiCard({ label, value, delta, trend, comparisonPeriod, sparklineData }: KpiCardProps) {
  return (
    <Card>
      <Card.Body className="space-y-2">
        <p className="text-xs font-semibold uppercase tracking-wider text-slate-500">{label}</p>

        <div className="flex items-end justify-between gap-3">
          <p className="text-2xl font-bold text-slate-950">{value}</p>

          {sparklineData && sparklineData.length > 1 && (
            <div className="h-10 w-20">
              <ReactApexChart
                options={SPARKLINE_OPTIONS}
                series={[{ name: label, data: sparklineData }]}
                type="line"
                height={40}
                width={80}
              />
            </div>
          )}
        </div>

        {(delta || comparisonPeriod) && (
          <p className="flex items-center gap-1.5 text-xs text-slate-500">
            {delta && (
              <span
                className={cn(
                  'font-semibold',
                  trend === 'down' ? 'text-status-danger' : 'text-status-success',
                )}
              >
                {delta}
              </span>
            )}
            {comparisonPeriod && <span>{comparisonPeriod}</span>}
          </p>
        )}
      </Card.Body>
    </Card>
  )
}
