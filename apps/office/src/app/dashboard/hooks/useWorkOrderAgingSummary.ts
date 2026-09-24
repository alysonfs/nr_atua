import { useEffect, useState } from 'react'
import { apiClient } from '../../../shared/lib/apiClient'

/**
 * RF-027: contagem de OS abertas por faixa de tempo de abertura (aging),
 * cumulativa e não mutuamente exclusiva entre as 3 faixas —
 * GET /api/tenants/{tenantId}/work-orders/aging-summary.
 */
export interface WorkOrderAgingSummary {
  moreThan5Days: number
  moreThan10Days: number
  moreThan30Days: number
}

/**
 * Busca a contagem de OS abertas por faixa de aging para o tenant ativo.
 *
 * `tenantId` deve vir do tenant ativo do usuário (ver useMyTenants(), no
 * padrão já usado por SettingsPage.tsx). Enquanto `tenantId` for `null`
 * (tenant ainda não resolvido), nenhuma requisição é disparada.
 */
export function useWorkOrderAgingSummary(tenantId: string | null) {
  const [summary, setSummary] = useState<WorkOrderAgingSummary | null>(null)
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
        const response = await apiClient.get<WorkOrderAgingSummary>(
          `/api/tenants/${tenantId}/work-orders/aging-summary`,
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
