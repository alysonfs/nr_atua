import { useEffect, useState } from 'react'
import { apiClient } from '../../../shared/lib/apiClient'

/**
 * RF-017: status de OS é uma string livre (sem enum fixo).
 *
 * GET /api/tenants/{tenantId}/work-orders/summary?month=YYYY-MM
 *
 * Ver docs/architecture/dashboard-work-order-queries.md (Endpoint 1) para o
 * contrato completo. O parsing abaixo extrai apenas `statuses` da resposta
 * (`{ month, timeZoneId, statuses: [...] }`), mantendo o mesmo shape que os
 * componentes de UI (StatusSummaryCard/SummaryMonth) já consumiam quando o
 * hook era mockado.
 */
export interface StatusMonthSummary {
  /** Nome cru do status, conforme informado pelo provedor. */
  status: string
  /** Total de OS no status ao longo do mês consultado. */
  total: number
  /** Quantidade de OS no status, por dia do mês (index 0 = dia 1). */
  dailyCounts: number[]
}

interface WorkOrderMonthlySummaryResponse {
  month: string
  timeZoneId: string
  statuses: StatusMonthSummary[]
}

function formatMonth(referenceDate: Date): string {
  const year = referenceDate.getFullYear()
  const month = String(referenceDate.getMonth() + 1).padStart(2, '0')
  return `${year}-${month}`
}

/**
 * Busca o sumário mensal (mês corrente, por padrão) de OS agrupadas por
 * status, com série diária para o mini-gráfico (sparkline) de cada card.
 *
 * `tenantId` deve vir do tenant ativo do usuário (ver useMyTenants(), no
 * padrão já usado por SettingsPage.tsx). Enquanto `tenantId` for `null`
 * (tenant ainda não resolvido), nenhuma requisição é disparada.
 */
export function useServiceOrderMonthSummary(tenantId: string | null, referenceDate: Date = new Date()) {
  const [summary, setSummary] = useState<StatusMonthSummary[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)

  const month = formatMonth(referenceDate)

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
        const response = await apiClient.get<WorkOrderMonthlySummaryResponse>(
          `/api/tenants/${tenantId}/work-orders/summary?month=${month}`,
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
  }, [tenantId, month])

  return { summary, isLoading, isError }
}
