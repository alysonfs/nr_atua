import { useEffect, useState } from 'react'
import { apiClient } from '../../../shared/lib/apiClient'

/**
 * RF-030: contagem de OS Em Aberto por prazo de próximo atendimento —
 * GET /api/tenants/{tenantId}/work-orders/next-appointment-summary.
 */
export interface WorkOrderNextAppointmentSummary {
  overdue: number
  dueToday: number
  dueTomorrow: number
}

/**
 * Busca a contagem de OS por prazo de próximo atendimento (vencido, vence
 * hoje, em atenção) para o tenant ativo.
 *
 * `tenantId` deve vir do tenant ativo do usuário (ver useMyTenants(), no
 * padrão já usado por SettingsPage.tsx). Enquanto `tenantId` for `null`
 * (tenant ainda não resolvido), nenhuma requisição é disparada.
 */
export function useWorkOrderNextAppointmentSummary(tenantId: string | null) {
  const [summary, setSummary] = useState<WorkOrderNextAppointmentSummary | null>(null)
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
        const response = await apiClient.get<WorkOrderNextAppointmentSummary>(
          `/api/tenants/${tenantId}/work-orders/next-appointment-summary`,
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
