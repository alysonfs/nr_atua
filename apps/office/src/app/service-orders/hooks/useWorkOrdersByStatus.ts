import { useEffect, useState } from 'react'
import { apiClient } from '../../../shared/lib/apiClient'

/**
 * RF-017/RF-027: status de OS é uma string livre (sem enum fixo). Este hook
 * expõe a listagem paginada de ordens de serviço filtradas por um status
 * exato OU por tempo mínimo de abertura em dias (aging) — mutuamente
 * exclusivos, exatamente um dos dois deve ser informado no filtro.
 *
 * GET /api/tenants/{tenantId}/work-orders?status=&page=&pageSize=
 * GET /api/tenants/{tenantId}/work-orders?minAgeDays=&page=&pageSize=
 *
 * Ver docs/architecture/dashboard-work-order-queries.md para o contrato
 * completo.
 */
export interface WorkOrderListItem {
  /** Identificador da OS (Atua). */
  id: string
  /** Identificador externo da OS no sistema de origem do provedor (workOrderId do iService). */
  workOrderProviderId: string
  /** Número visível da OS no provedor (workOrderNo do iService, ex.: BRWO260909869). */
  workOrderProviderNo: string | null
  /** Status cru da OS (RF-017). */
  status: string
  /** Data/hora de criação da OS no Atua (ISO 8601). */
  createdAt: string
  /** Data/hora da última atualização da OS no Atua (ISO 8601). */
  updatedAt: string
  /** Data de criação da OS informada pelo provedor (ISO 8601). */
  providerCreatedAt: string | null
  /** Modelo do equipamento. */
  productModel: string | null
  /** Marca do equipamento. */
  productBrand: string | null
  /** Nome do consumidor. */
  customerName: string | null
  /** Cidade do endereço do consumidor. */
  cityName: string | null
  /** Nome do provedor de integração (ex.: iService). */
  providerName: string
}

/** Filtro aplicado à listagem: status exato XOR tempo mínimo de abertura (dias). */
export interface WorkOrdersFilter {
  status?: string
  minAgeDays?: number
}

interface WorkOrderListResponse {
  status: string | null
  minAgeDays: number | null
  page: number
  pageSize: number
  totalCount: number
  items: WorkOrderListItem[]
}

/**
 * Busca a lista paginada de OS filtradas por status exato ou por aging para
 * o tenant ativo.
 *
 * `tenantId` deve vir do tenant ativo do usuário (ver useMyTenants(), no
 * padrão já usado por SettingsPage.tsx). Enquanto `tenantId` for `null`
 * (tenant ainda não resolvido), nenhuma requisição é disparada.
 */
export function useWorkOrdersByStatus(
  tenantId: string | null,
  filter: WorkOrdersFilter,
  page: number,
  pageSize: number,
) {
  const { status, minAgeDays } = filter
  const [orders, setOrders] = useState<WorkOrderListItem[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      if (!tenantId || (!status && !minAgeDays)) {
        setIsLoading(false)
        setOrders([])
        setTotalCount(0)
        return
      }

      setIsLoading(true)
      setIsError(false)

      try {
        const filterParam = status
          ? `status=${encodeURIComponent(status)}`
          : `minAgeDays=${minAgeDays}`
        const response = await apiClient.get<WorkOrderListResponse>(
          `/api/tenants/${tenantId}/work-orders?${filterParam}&page=${page}&pageSize=${pageSize}`,
        )
        if (isCancelled) return
        setOrders(response?.items ?? [])
        setTotalCount(response?.totalCount ?? 0)
      } catch {
        if (isCancelled) return
        setIsError(true)
        setOrders([])
        setTotalCount(0)
      } finally {
        if (!isCancelled) setIsLoading(false)
      }
    })

    return () => {
      isCancelled = true
    }
  }, [tenantId, status, minAgeDays, page, pageSize])

  return { orders, totalCount, isLoading, isError }
}

