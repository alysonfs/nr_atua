import { useEffect, useState } from 'react'
import { apiClient, ApiError } from '../../../shared/lib/apiClient'

/** Entrada de `work_order_history` (RF-026.6), já ordenada do mais antigo para o mais novo. */
export interface WorkOrderHistoryEntry {
  status: string
  createdAt: string
}

/**
 * RF-026.5/RF-026.6: detalhe completo de uma OS, incluindo campos normalizados
 * e o histórico de transições de status.
 *
 * GET /api/tenants/{tenantId}/work-orders/{workOrderId}
 */
export interface WorkOrderDetail {
  id: string
  workOrderProviderId: string
  workOrderProviderNo: string | null
  status: string
  createdAt: string
  updatedAt: string
  providerCreatedAt: string | null
  providerUpdatedAt: string | null
  serviceRequestId: string | null
  amount: number | null
  customerType: string | null
  customerName: string | null
  customerCpf: string | null
  contactEmail: string | null
  contactPhone: string | null
  contactName: string | null
  address: string | null
  zipCode: string | null
  countryName: string | null
  stateName: string | null
  cityName: string | null
  productBrand: string | null
  pdCode: string | null
  categoryId: string | null
  productCategoryCode: string | null
  productCode: string | null
  productModel: string | null
  productStatus: string | null
  symptom: string | null
  history: WorkOrderHistoryEntry[]
}

/**
 * Busca o detalhe de uma OS específica para o tenant ativo.
 *
 * `tenantId`/`workOrderId` devem vir resolvidos (ver useMyTenants() e
 * useParams()). Enquanto qualquer um deles for `null`, nenhuma requisição é
 * disparada. 404 é tratado separadamente (`notFound`) para permitir que a UI
 * exiba uma mensagem amigável em vez do estado de erro genérico.
 */
export function useWorkOrderDetail(tenantId: string | null, workOrderId: string | null) {
  const [detail, setDetail] = useState<WorkOrderDetail | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)
  const [notFound, setNotFound] = useState(false)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      if (!tenantId || !workOrderId) {
        setIsLoading(false)
        setDetail(null)
        setIsError(false)
        setNotFound(false)
        return
      }

      setIsLoading(true)
      setIsError(false)
      setNotFound(false)

      try {
        const response = await apiClient.get<WorkOrderDetail>(
          `/api/tenants/${tenantId}/work-orders/${workOrderId}`,
        )
        if (isCancelled) return
        setDetail(response)
      } catch (err) {
        if (isCancelled) return
        setDetail(null)
        if (err instanceof ApiError && err.status === 404) {
          setNotFound(true)
        } else {
          setIsError(true)
        }
      } finally {
        if (!isCancelled) setIsLoading(false)
      }
    })

    return () => {
      isCancelled = true
    }
  }, [tenantId, workOrderId])

  return { detail, isLoading, isError, notFound }
}
