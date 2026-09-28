import { useEffect, useState } from 'react'
import { apiClient } from '../../../shared/lib/apiClient'

/**
 * RF-032: contagem de OS Em Aberto agrupada por substatus, com o
 * responsável já resolvido (RF-031.2) —
 * GET /api/tenants/{tenantId}/work-orders/substatus-summary.
 *
 * `subStatus` já vem pronto para exibição (o backend substitui `null` por
 * "Sem substatus" — ver `WorkOrderSubStatusSummaryQueryHandler`), não é
 * necessário tratamento de nulo no frontend.
 */
export interface WorkOrderSubStatusCount {
  subStatus: string
  /** Enum `EWorkOrderSubStatusResponsible` serializado como string (ex.: "PostoAutorizado"). */
  responsible: string
  total: number
}

interface WorkOrderSubStatusSummaryResponse {
  groups: WorkOrderSubStatusCount[]
}

/**
 * Busca a contagem de OS por substatus/responsável para o tenant ativo.
 *
 * `tenantId` deve vir do tenant ativo do usuário (ver useMyTenants(), no
 * padrão já usado por SettingsPage.tsx). Enquanto `tenantId` for `null`
 * (tenant ainda não resolvido), nenhuma requisição é disparada.
 */
export function useWorkOrderSubStatusSummary(tenantId: string | null) {
  const [groups, setGroups] = useState<WorkOrderSubStatusCount[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      if (!tenantId) {
        setIsLoading(false)
        setGroups([])
        return
      }

      setIsLoading(true)
      setIsError(false)

      try {
        const response = await apiClient.get<WorkOrderSubStatusSummaryResponse>(
          `/api/tenants/${tenantId}/work-orders/substatus-summary`,
        )
        if (isCancelled) return
        setGroups(response?.groups ?? [])
      } catch {
        if (isCancelled) return
        setIsError(true)
        setGroups([])
      } finally {
        if (!isCancelled) setIsLoading(false)
      }
    })

    return () => {
      isCancelled = true
    }
  }, [tenantId])

  return { groups, isLoading, isError }
}
