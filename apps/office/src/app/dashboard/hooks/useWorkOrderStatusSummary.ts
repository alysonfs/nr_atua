import { useEffect, useState } from 'react'
import { apiClient } from '../../../shared/lib/apiClient'

/**
 * RF-017/ADR-027: contagem de OS por status ATUAL (sem recorte de mês nem
 * série diária) — GET /api/tenants/{tenantId}/work-orders/status-summary.
 */
export interface StatusCount {
  /** Nome cru do status, conforme informado pelo provedor. */
  status: string
  /** Total de OS atualmente nesse status. */
  total: number
}

interface WorkOrderStatusSummaryResponse {
  statuses: StatusCount[]
}

/**
 * Busca a contagem de OS por status atual, sem recorte de mês/série diária
 * (ADR-027 — substitui o antigo `useServiceOrderMonthSummary`).
 *
 * `tenantId` deve vir do tenant ativo do usuário (ver useMyTenants(), no
 * padrão já usado por SettingsPage.tsx). Enquanto `tenantId` for `null`
 * (tenant ainda não resolvido), nenhuma requisição é disparada.
 */
export function useWorkOrderStatusSummary(tenantId: string | null) {
  const [summary, setSummary] = useState<StatusCount[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      if (!tenantId) {
        setIsLoading(false)
        setSummary([])
        return
      }

      setIsLoading(true)
      setIsError(false)

      try {
        const response = await apiClient.get<WorkOrderStatusSummaryResponse>(
          `/api/tenants/${tenantId}/work-orders/status-summary`,
        )
        if (isCancelled) return
        setSummary(response?.statuses ?? [])
      } catch {
        if (isCancelled) return
        setIsError(true)
        setSummary([])
      } finally {
        if (!isCancelled) setIsLoading(false)
      }
    })

    return () => {
      isCancelled = true
    }
  }, [tenantId])

  return { summary, isLoading, isError }
}
