/**
 * Período do resumo de consumo/custo de IA (`/api/llm-usage/summary`).
 * Opções fixas oferecidas na página: últimos 7/30/90 dias (`to` inclusivo,
 * hoje) e mês corrente (do dia 1 até hoje). Sem seleção livre de datas —
 * mantém o contrato simples e evita estados intermediários inválidos.
 */
export type AiUsagePeriod = '7d' | '30d' | '90d' | 'currentMonth'

export const AI_USAGE_PERIODS: AiUsagePeriod[] = ['7d', '30d', '90d', 'currentMonth']

/** Formata uma data local como `YYYY-MM-DD`, sem conversão de fuso (evita o "dia errado" do `toISOString`). */
function toDateOnly(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

/** Resolve `{ from, to }` (ambos inclusivos) para o período selecionado, com base em `now` (hoje, por padrão). */
export function resolveAiUsageRange(period: AiUsagePeriod, now: Date = new Date()): { from: string; to: string } {
  const to = toDateOnly(now)

  if (period === 'currentMonth') {
    const firstOfMonth = new Date(now.getFullYear(), now.getMonth(), 1)
    return { from: toDateOnly(firstOfMonth), to }
  }

  const days = period === '7d' ? 7 : period === '30d' ? 30 : 90
  const from = new Date(now)
  from.setDate(from.getDate() - (days - 1))
  return { from: toDateOnly(from), to }
}
