import { useEffect, useState } from 'react'
import { apiClient, ApiError } from '../../../shared/lib/apiClient'
import { mockWorkOrderMetricsSummary } from '../../../shared/mocks/workOrderMetricsSummary'

/**
 * Ponto mensal da série de OS criadas x concluídas.
 */
export interface WorkOrderTrendPoint {
  /** Mês no formato "AAAA-MM". */
  month: string
  /** OS criadas no mês. */
  created: number
  /** OS concluídas no mês. */
  completed: number
}

/** Distribuição de OS por status atual (contagem total, sem recorte de mês). */
export interface WorkOrderStatusDistributionItem {
  /** Status cru, conforme informado pelo provedor (RF-017). */
  status: string
  /** Total de OS atualmente nesse status. */
  total: number
}

export interface WorkOrderMetricsSummary {
  trend: WorkOrderTrendPoint[]
  statusDistribution: WorkOrderStatusDistributionItem[]
}

const DEFAULT_MONTHS = 12

/**
 * Busca a série histórica de OS criadas/concluídas e a distribuição por
 * status para alimentar os gráficos do Dashboard.
 *
 * GET /api/tenants/{tenantId}/work-orders/metrics-summary?months=12
 *
 * O endpoint ainda está sendo implementado pelo backend em paralelo: se a
 * chamada falhar em ambiente de desenvolvimento (ex.: 404, rota inexistente),
 * o hook cai para um mock local (`shared/mocks/workOrderMetricsSummary.ts`)
 * com o mesmo shape do contrato, para não bloquear a UI. Assim que o
 * endpoint real existir, a chamada passa a funcionar sem qualquer mudança
 * neste hook.
 */
export function useWorkOrderMetricsSummary(tenantId: string | null, months: number = DEFAULT_MONTHS) {
  const [data, setData] = useState<WorkOrderMetricsSummary | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      if (!tenantId) {
        setIsLoading(false)
        setData(null)
        return
      }

      setIsLoading(true)
      setIsError(false)

      try {
        const response = await apiClient.get<WorkOrderMetricsSummary>(
          `/api/tenants/${tenantId}/work-orders/metrics-summary?months=${months}`,
        )
        if (isCancelled) return
        setData(response ?? { trend: [], statusDistribution: [] })
      } catch (error) {
        if (isCancelled) return

        const isEndpointNotReady = error instanceof ApiError && error.status === 404
        if (isEndpointNotReady && import.meta.env.DEV) {
          setData(mockWorkOrderMetricsSummary)
          return
        }

        setIsError(true)
        setData(null)
      } finally {
        if (!isCancelled) setIsLoading(false)
      }
    })

    return () => {
      isCancelled = true
    }
  }, [tenantId, months])

  return { data, isLoading, isError }
}
