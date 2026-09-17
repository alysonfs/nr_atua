import { useCallback, useEffect, useState } from 'react'
import { apiClient, ApiError } from '../../../shared/lib/apiClient'
import type {
  RecurrentCollectionIntervalErrorCode,
  RecurrentCollectionIntervalView,
} from '../../../shared/types/integration'

/**
 * RF-025: leitura do intervalo de coleta recorrente configurado na
 * integração (em minutos). Quando não houver valor persistido, a API já
 * retorna o padrão (15 minutos).
 *
 * GET /api/tenants/{tenantId}/integrations/{integrationId}/recurrent-collection-interval
 */
export function useRecurrentCollectionInterval(
  tenantId: string | null,
  integrationId: string | null,
) {
  const [interval, setInterval] = useState<RecurrentCollectionIntervalView | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      if (!tenantId || !integrationId) {
        setIsLoading(false)
        setInterval(null)
        return
      }

      setIsLoading(true)
      setIsError(false)

      try {
        const response = await apiClient.get<RecurrentCollectionIntervalView>(
          `/api/tenants/${tenantId}/integrations/${integrationId}/recurrent-collection-interval`,
        )
        if (isCancelled) return
        setInterval(response)
      } catch {
        if (isCancelled) return
        setIsError(true)
        setInterval(null)
      } finally {
        if (!isCancelled) setIsLoading(false)
      }
    })

    return () => {
      isCancelled = true
    }
  }, [tenantId, integrationId, reloadToken])

  const refetch = useCallback(async () => {
    setReloadToken((token) => token + 1)
  }, [])

  return { interval, isLoading, isError, refetch }
}

export interface UpdateRecurrentCollectionIntervalResult {
  status: 'success' | 'error'
  view?: RecurrentCollectionIntervalView
  errorCode?: RecurrentCollectionIntervalErrorCode
}

/**
 * RF-025.4/RF-025.5: persiste o novo intervalo de coleta recorrente.
 *
 * PUT /api/tenants/{tenantId}/integrations/{integrationId}/recurrent-collection-interval
 */
export function useUpdateRecurrentCollectionInterval(
  tenantId: string | null,
  integrationId: string | null,
) {
  const [isSaving, setIsSaving] = useState(false)

  const update = useCallback(
    async (recurrentCollectionIntervalMinutes: number): Promise<UpdateRecurrentCollectionIntervalResult> => {
      if (!tenantId || !integrationId) {
        return { status: 'error', errorCode: 'integration_not_found' }
      }

      setIsSaving(true)
      try {
        const view = await apiClient.put<RecurrentCollectionIntervalView>(
          `/api/tenants/${tenantId}/integrations/${integrationId}/recurrent-collection-interval`,
          { recurrentCollectionIntervalMinutes },
        )
        return { status: 'success', view: view ?? undefined }
      } catch (err) {
        if (err instanceof ApiError) {
          if (err.status === 403) {
            return { status: 'error', errorCode: 'forbidden' }
          }
          return {
            status: 'error',
            errorCode: (err.code as RecurrentCollectionIntervalErrorCode) ?? 'unknown_error',
          }
        }
        return { status: 'error', errorCode: 'unknown_error' }
      } finally {
        setIsSaving(false)
      }
    },
    [tenantId, integrationId],
  )

  return { update, isSaving }
}
