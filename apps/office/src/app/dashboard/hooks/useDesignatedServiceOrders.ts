import { useEffect, useState } from 'react'
import { apiClient } from '../../../shared/lib/apiClient'

/**
 * RF-017: status de OS é uma string livre (sem enum fixo). Este hook expõe
 * as ordens de serviço atualmente com status "Designado" para exibição na
 * tabela do Dashboard.
 *
 * GET /api/tenants/{tenantId}/work-orders?status=Designado&page=1&pageSize=100
 *
 * Ver docs/architecture/dashboard-work-order-queries.md (Endpoint 2) para o
 * contrato completo. Decisão: sem paginação visual por ora, usamos
 * `pageSize=100` (o máximo aceito pela API) numa única página — o volume
 * esperado de OS designadas simultaneamente é baixo no MVP; se isso mudar,
 * a UI de paginação deve ser adicionada em etapa futura.
 *
 * `providerId` é o identificador EXTERNO da própria OS no sistema de
 * origem (não o nome/empresa do provedor) — por isso não existe
 * `providerName` no contrato real.
 */
export interface DesignatedServiceOrder {
  /** Identificador da OS (Atua). */
  id: string
  /** Identificador externo da OS no sistema de origem do provedor. */
  providerId: string
  /** Status cru da OS (RF-017). Nesta tabela, sempre "Designado". */
  status: string
  /** Data/hora de criação da OS (ISO 8601). */
  createdAt: string
  /** Data/hora da última atualização da OS (ISO 8601). */
  updatedAt: string
}

interface WorkOrderListResponse {
  status: string
  page: number
  pageSize: number
  totalCount: number
  items: DesignatedServiceOrder[]
}

const DESIGNATED_STATUS = 'Designado'
const PAGE_SIZE = 100

/**
 * Busca a lista de OS com status "Designado" do tenant ativo.
 *
 * `tenantId` deve vir do tenant ativo do usuário (ver useMyTenants(), no
 * padrão já usado por SettingsPage.tsx). Enquanto `tenantId` for `null`
 * (tenant ainda não resolvido), nenhuma requisição é disparada.
 */
export function useDesignatedServiceOrders(tenantId: string | null) {
  const [orders, setOrders] = useState<DesignatedServiceOrder[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      if (!tenantId) {
        setIsLoading(false)
        setOrders([])
        return
      }

      setIsLoading(true)
      setIsError(false)

      try {
        const response = await apiClient.get<WorkOrderListResponse>(
          `/api/tenants/${tenantId}/work-orders?status=${DESIGNATED_STATUS}&page=1&pageSize=${PAGE_SIZE}`,
        )
        if (isCancelled) return
        setOrders(response?.items ?? [])
      } catch {
        if (isCancelled) return
        setIsError(true)
        setOrders([])
      } finally {
        if (!isCancelled) setIsLoading(false)
      }
    })

    return () => {
      isCancelled = true
    }
  }, [tenantId])

  return { orders, isLoading, isError }
}
