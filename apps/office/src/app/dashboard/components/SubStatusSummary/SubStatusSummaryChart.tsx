import type { ApexOptions } from 'apexcharts'
import ReactApexChart from 'react-apexcharts'
import { useTranslation } from 'react-i18next'
import type { WorkOrderSubStatusCount } from '../../hooks/useWorkOrderSubStatusSummary'

/**
 * Paleta de cores por responsável (RF-031.2/RF-032.2): decisão de
 * implementação — o RF não define cores exatas, apenas exige "uma cor por
 * responsável, com legenda visível". Cada um dos 7 responsáveis do
 * catálogo mais "Não mapeado" recebe uma cor fixa e distinta.
 */
const RESPONSIBLE_COLORS: Record<string, string> = {
  PostoAutorizado: '#0ea5e9',
  MideaCallcenter: '#7c3aed',
  MideaPlanejamento: '#16a34a',
  MideaCorreios: '#d97706',
  MideaJira: '#0891b2',
  MideaJuridico: '#dc2626',
  MideaSuporteTecnico: '#db2777',
  NaoMapeado: '#64748b',
}

const FALLBACK_COLOR = RESPONSIBLE_COLORS.NaoMapeado

/** Rótulo usado pelo backend para OS sem substatus mapeado (ver WorkOrderSubStatusSummaryQueryHandler.NoSubStatusLabel). */
export const NO_SUBSTATUS_LABEL = 'Sem substatus'

/** Cor associada a um responsável, com fallback para responsáveis desconhecidos. */
function colorForResponsible(responsible: string): string {
  return RESPONSIBLE_COLORS[responsible] ?? FALLBACK_COLOR
}

interface SubStatusSummaryChartProps {
  /** Contagem de OS por substatus, com o responsável já resolvido (RF-031.2). */
  groups: WorkOrderSubStatusCount[]
}

/**
 * Gráfico de barras horizontais por substatus (RF-032.2), colorido por
 * responsável, com legenda de cores abaixo do gráfico (apexcharts não
 * suporta legenda nativa em modo `distributed`, então a legenda é
 * construída manualmente a partir dos responsáveis presentes nos dados).
 */
export function SubStatusSummaryChart({ groups }: SubStatusSummaryChartProps) {
  const { t } = useTranslation()

  const sorted = groups
    .filter((group) => group.subStatus !== NO_SUBSTATUS_LABEL)
    .sort((a, b) => b.total - a.total)
  const colors = sorted.map((group) => colorForResponsible(group.responsible))
  const responsiblesInUse = Array.from(new Set(sorted.map((group) => group.responsible)))
  // Substatus vindo do provedor em inglês (fallback do Collector, ver
  // IServiceProviderInteractionOrderAdapter.ExtractSubStatus) é traduzido aqui; sem
  // correspondência confirmada, mantém o texto original (ver dashboard.subStatus.values).
  const translatedLabel = (subStatus: string) => t(`dashboard.subStatus.values.${subStatus}`, subStatus)

  const options: ApexOptions = {
    chart: { type: 'bar', toolbar: { show: false }, fontFamily: 'var(--font-sans)' },
    colors,
    plotOptions: {
      bar: { distributed: true, borderRadius: 4, horizontal: true, barHeight: '70%' },
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
      categories: sorted.map((group) => translatedLabel(group.subStatus)),
      axisBorder: { show: false },
      axisTicks: { show: false },
      labels: { style: { fontSize: '11px' } },
    },
    yaxis: { labels: { style: { fontSize: '12px' } } },
    tooltip: { intersect: false },
  }

  const series = [{ name: 'total', data: sorted.map((group) => group.total) }]
  const chartHeight = Math.max(280, sorted.length * 36)

  return (
    <div className="space-y-3">
      <ReactApexChart options={options} series={series} type="bar" height={chartHeight} />
      <ul className="flex flex-wrap gap-x-4 gap-y-2 text-xs text-slate-600">
        {responsiblesInUse.map((responsible) => (
          <li key={responsible} className="flex items-center gap-1.5">
            <span
              aria-hidden="true"
              className="inline-block h-2.5 w-2.5 rounded-full"
              style={{ backgroundColor: colorForResponsible(responsible) }}
            />
            {t(`dashboard.subStatus.responsibles.${responsible}`, responsible)}
          </li>
        ))}
      </ul>
    </div>
  )
}
