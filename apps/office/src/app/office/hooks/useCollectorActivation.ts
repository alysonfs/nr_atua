import { useCallback, useEffect, useState } from 'react'
import { apiClient, ApiError } from '../../../shared/lib/apiClient'
import type {
  ActivateCollectorErrorCode,
  CollectorActivationView,
  DeactivateCollectorErrorCode,
} from '../../../shared/types/integration'

/**
 * RF-008 (ADR-020/ADR-024): estado de ativação do Agente Coletor.
 *
 * GET /api/tenants/{tenantId}/integrations/{integrationId}/collector-activation
 */
export function useCollectorActivation(tenantId: string | null, integrationId: string | null) {
  const [status, setStatus] = useState<CollectorActivationView | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      if (!tenantId || !integrationId) {
        setIsLoading(false)
        setStatus(null)
        return
      }

      setIsLoading(true)
      setIsError(false)

      try {
        const response = await apiClient.get<CollectorActivationView>(
          `/api/tenants/${tenantId}/integrations/${integrationId}/collector-activation`,
        )
        if (isCancelled) return
        setStatus(response)
      } catch {
        if (isCancelled) return
        setIsError(true)
        setStatus(null)
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

  return { status, isLoading, isError, refetch }
}

export interface ActivateCollectorResult {
  status: 'success' | 'error'
  view?: CollectorActivationView
  errorCode?: ActivateCollectorErrorCode
}

/**
 * RF-008/ADR-020: ativação do Agente Coletor.
 *
 * PUT /api/tenants/{tenantId}/integrations/{integrationId}/collector-activation
 *
 * Operação idempotente controlada por header `Idempotency-Key`, gerado a
 * cada tentativa de ativação a partir desta chamada.
 */
export function useActivateCollector(tenantId: string | null, integrationId: string | null) {
  const [isActivating, setIsActivating] = useState(false)

  const activate = useCallback(async (): Promise<ActivateCollectorResult> => {
    if (!tenantId || !integrationId) {
      return { status: 'error', errorCode: 'integration_not_found' }
    }

    setIsActivating(true)
    try {
      const view = await apiClient.put<CollectorActivationView>(
        `/api/tenants/${tenantId}/integrations/${integrationId}/collector-activation`,
        undefined,
        { 'Idempotency-Key': crypto.randomUUID() },
      )
      return { status: 'success', view: view ?? undefined }
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 403) {
          return { status: 'error', errorCode: 'forbidden' }
        }
        return {
          status: 'error',
          errorCode: (err.code as ActivateCollectorErrorCode) ?? 'unknown_error',
        }
      }
      return { status: 'error', errorCode: 'unknown_error' }
    } finally {
      setIsActivating(false)
    }
  }, [tenantId, integrationId])

  return { activate, isActivating }
}

export interface DeactivateCollectorResult {
  status: 'success' | 'error'
  view?: CollectorActivationView
  errorCode?: DeactivateCollectorErrorCode
}

/**
 * RF-008.5/ADR-020: desativação manual do Agente Coletor.
 *
 * DELETE /api/tenants/{tenantId}/integrations/{integrationId}/collector-activation
 *
 * Operação idempotente controlada por header `Idempotency-Key`, gerado a
 * cada tentativa de desativação a partir desta chamada.
 */
export function useDeactivateCollector(tenantId: string | null, integrationId: string | null) {
  const [isDeactivating, setIsDeactivating] = useState(false)

  const deactivate = useCallback(async (): Promise<DeactivateCollectorResult> => {
    if (!tenantId || !integrationId) {
      return { status: 'error', errorCode: 'integration_not_found' }
    }

    setIsDeactivating(true)
    try {
      const view = await apiClient.delete<CollectorActivationView>(
        `/api/tenants/${tenantId}/integrations/${integrationId}/collector-activation`,
        { 'Idempotency-Key': crypto.randomUUID() },
      )
      return { status: 'success', view: view ?? undefined }
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 403) {
          return { status: 'error', errorCode: 'forbidden' }
        }
        return {
          status: 'error',
          errorCode: (err.code as DeactivateCollectorErrorCode) ?? 'unknown_error',
        }
      }
      return { status: 'error', errorCode: 'unknown_error' }
    } finally {
      setIsDeactivating(false)
    }
  }, [tenantId, integrationId])

  return { deactivate, isDeactivating }
}
