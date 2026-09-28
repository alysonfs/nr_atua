import { useEffect, useState } from 'react'
import { apiClient } from '../../../shared/lib/apiClient'

/**
 * RF-033: contagem de OS Em Aberto em Situação Crítica (+5 dias sem
 * atualização) e em Risco de Criticidade (4 dias sem atualização) —
 * GET /api/tenants/{tenantId}/work-orders/criticality-summary.
 */
export interface WorkOrderCriticalitySummary {
  critical: number
  atRisk: number
}

/**
 * Busca a contagem de OS em situação crítica e em risco de criticidade
 * para o tenant ativo.
 *
 * `tenantId` deve vir do tenant ativo do usuário (ver useMyTenants(), no
 * padrão já usado por SettingsPage.tsx). Enquanto `tenantId` for `null`
 * (tenant ainda não resolvido), nenhuma requisição é disparada.
 */
export function useWorkOrderCriticalitySummary(tenantId: string | null) {
  const [summary, setSummary] = useState<WorkOrderCriticalitySummary | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      if (!tenantId) {
        setIsLoading(false)
        setSummary(null)
        return
      }

      setIsLoading(true)
      setIsError(false)

      try {
        const response = await apiClient.get<WorkOrderCriticalitySummary>(
          `/api/tenants/${tenantId}/work-orders/criticality-summary`,
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
  }, [tenantId])

  return { summary, isLoading, isError }
}
