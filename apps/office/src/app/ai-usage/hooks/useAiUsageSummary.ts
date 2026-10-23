import { useEffect, useState } from 'react'
import { apiClient } from '../../../shared/lib/apiClient'
import type { AiUsageSummaryResponse } from '../../../shared/types/aiUsage'

/**
 * Resumo de consumo e custo estimado de IA (LLM) por tenant, no período
 * informado (ambos inclusivos). `from`/`to` em `YYYY-MM-DD`.
 *
 * GET /api/llm-usage/summary?from={from}&to={to}
 */
export function useAiUsageSummary(from: string, to: string) {
  const [summary, setSummary] = useState<AiUsageSummaryResponse | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      setIsLoading(true)
      setIsError(false)

      try {
        const params = new URLSearchParams({ from, to })
        const response = await apiClient.get<AiUsageSummaryResponse>(
          `/api/llm-usage/summary?${params.toString()}`,
        )
        if (isCancelled) return
        setSummary(response ?? null)
      } catch {
        if (isCancelled) return
        setIsError(true)
        setSummary(null)
      } finally {
        if (!isCancelled) setIsLoading(false)
      }
    })

    return () => {
      isCancelled = true
    }
  }, [from, to, reloadToken])

  const refetch = () => setReloadToken((token) => token + 1)

  return { summary, isLoading, isError, refetch }
}
