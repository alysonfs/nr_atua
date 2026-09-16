import { useCallback, useEffect, useState } from 'react'
import { apiClient, ApiError } from '../../../shared/lib/apiClient'
import type {
  CreateTenantErrorCode,
  CreateTenantResponse,
  GetMyTenantsResponse,
  TenantMembershipDTO,
} from '../../../shared/types/tenant'

/**
 * RF-005.2 / ADR-018: resolução de tenant ativo.
 *
 * GET /api/users/me/tenants
 * - lista vazia + defaultTenantId null  -> usuário sem tenant (RF-006)
 * - 1 tenant                            -> seleção automática
 * - >1 tenants                          -> seleção explícita exigida
 */
export function useMyTenants() {
  const [tenants, setTenants] = useState<TenantMembershipDTO[]>([])
  const [defaultTenantId, setDefaultTenantId] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      setIsLoading(true)
      setIsError(false)

      try {
        const response = await apiClient.get<GetMyTenantsResponse>('/api/users/me/tenants')
        if (isCancelled) return
        setTenants(response?.tenants ?? [])
        setDefaultTenantId(response?.defaultTenantId ?? null)
      } catch {
        if (isCancelled) return
        setIsError(true)
        setTenants([])
        setDefaultTenantId(null)
      } finally {
        if (!isCancelled) setIsLoading(false)
      }
    })

    return () => {
      isCancelled = true
    }
  }, [reloadToken])

  const refetch = useCallback(() => {
    setReloadToken((token) => token + 1)
  }, [])

  return {
    tenants,
    defaultTenantId,
    isLoading,
    isError,
    hasNoTenant: !isLoading && !isError && tenants.length === 0,
    requiresSelection: !isLoading && !isError && tenants.length > 1,
    refetch,
  }
}

export interface CreateTenantResult {
  status: 'success' | 'error'
  tenantId?: string
  integrationId?: string
  errorCode?: CreateTenantErrorCode
}

/**
 * RF-006.1: criação do tenant na primeira integração (nome + CNPJ).
 *
 * POST /api/tenants
 */
export function useCreateTenant() {
  const [isSubmitting, setIsSubmitting] = useState(false)

  const createTenant = useCallback(
    async (name: string, cnpj: string): Promise<CreateTenantResult> => {
      setIsSubmitting(true)
      try {
        const response = await apiClient.post<CreateTenantResponse>('/api/tenants', {
          name,
          cnpj,
        })
        return {
          status: 'success',
          tenantId: response?.tenantId,
          integrationId: response?.integrationId,
        }
      } catch (err) {
        if (err instanceof ApiError) {
          return {
            status: 'error',
            errorCode: (err.code as CreateTenantErrorCode) ?? 'unknown_error',
          }
        }
        return { status: 'error', errorCode: 'unknown_error' }
      } finally {
        setIsSubmitting(false)
      }
    },
    [],
  )

  return { createTenant, isSubmitting }
}
