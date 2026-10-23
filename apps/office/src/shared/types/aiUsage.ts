/**
 * Consumo e custo de IA (LLM) por tenant — página "Inteligência Artificial".
 *
 * Tipos espelhando o contrato de `GET /api/llm-usage/summary` (serialização
 * camelCase padrão do ASP.NET Core minimal APIs). Custo estimado com base no
 * preço público por token do provedor do modelo; a fatura AWS é a
 * referência oficial.
 */

export interface AiUsageTotals {
  calls: number
  inputTokens: number
  outputTokens: number
  estimatedCost: number
  avgLatencyMs: number
  failedCalls: number
}

export interface AiUsageTenant {
  tenantId: string
  tenantName: string
  calls: number
  inputTokens: number
  outputTokens: number
  estimatedCost: number
  avgLatencyMs: number
  failedCalls: number
}

export interface AiUsageDailyPoint {
  date: string
  calls: number
  inputTokens: number
  outputTokens: number
  estimatedCost: number
}

export interface AiUsageModel {
  modelId: string
  calls: number
  inputTokens: number
  outputTokens: number
  estimatedCost: number
}

export interface AiUsageSummaryResponse {
  from: string
  to: string
  currency: string
  totals: AiUsageTotals
  tenants: AiUsageTenant[]
  daily: AiUsageDailyPoint[]
  models: AiUsageModel[]
}
