/**
 * RF-017: status de OS é uma string livre definida pelo provedor (sem enum
 * fixo). Os nomes abaixo são exemplos plausíveis de status "crus" usados
 * para popular o dashboard enquanto a integração com o endpoint real de
 * agregação mensal não é implementada (etapa futura).
 *
 * Este hook gera dados MOCKADOS de forma determinística (sem Math.random),
 * agrupando a contagem de OS por status, por dia, para o mês corrente.
 * Quando a API de sumário mensal existir, este hook deve ser substituído
 * por uma integração real, mantendo o mesmo formato de retorno.
 */

export interface StatusMonthSummary {
  /** Nome cru do status, conforme informado pelo provedor. */
  status: string
  /** Total de OS no status ao longo do mês corrente. */
  total: number
  /** Quantidade de OS no status, por dia do mês (index 0 = dia 1). */
  dailyCounts: number[]
}

const TRACKED_STATUSES = ['Designado', 'Em Processamento', 'Concluído', 'Cancelado'] as const

/**
 * Gera um valor determinístico e plausível para a contagem de um status em
 * um dado dia do mês, sem depender de números aleatórios reais.
 *
 * A fórmula combina uma onda senoidal (para simular variação natural ao
 * longo do mês) com um deslocamento por status, garantindo resultados
 * estáveis entre execuções e testes.
 */
function computeDailyCount(statusIndex: number, dayOfMonth: number): number {
  const base = 4 + statusIndex * 2
  const wave = Math.sin((dayOfMonth + statusIndex * 3) / 2.5)
  const amplitude = 3 + statusIndex
  const value = base + wave * amplitude
  return Math.max(0, Math.round(value))
}

function getDaysInMonth(year: number, monthIndex: number): number {
  return new Date(year, monthIndex + 1, 0).getDate()
}

/**
 * Retorna o sumário mensal (mês corrente) de OS agrupadas por status, com
 * série diária para o mini-gráfico (sparkline) de cada card.
 */
export function useServiceOrderMonthSummary(referenceDate: Date = new Date()): StatusMonthSummary[] {
  const year = referenceDate.getFullYear()
  const monthIndex = referenceDate.getMonth()
  const daysInMonth = getDaysInMonth(year, monthIndex)

  return TRACKED_STATUSES.map((status, statusIndex) => {
    const dailyCounts: number[] = []

    for (let day = 1; day <= daysInMonth; day += 1) {
      dailyCounts.push(computeDailyCount(statusIndex, day))
    }

    const total = dailyCounts.reduce((sum, count) => sum + count, 0)

    return { status, total, dailyCounts }
  })
}
