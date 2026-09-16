import { useCallback, useEffect, useState } from 'react'
import { apiClient, ApiError } from '../../../shared/lib/apiClient'
import type {
  GetIServiceCredentialsResponse,
  SetCredentialsErrorCode,
  SetIServiceCredentialsRequest,
  ValidateCredentialsErrorCode,
  ValidateIServiceCredentialsResponse,
} from '../../../shared/types/integration'

/**
 * RF-006/RF-007 (ADR-018): status de credenciais iService.
 *
 * GET /api/tenants/{tenantId}/integrations/{integrationId}/credentials
 *
 * Nunca lê nem expõe o segredo (usuário/senha) — apenas hasCredentials,
 * validationStatus e timestamps.
 */
export function useIServiceCredentials(tenantId: string | null, integrationId: string | null) {
  const [status, setStatus] = useState<GetIServiceCredentialsResponse | null>(null)
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
        const response = await apiClient.get<GetIServiceCredentialsResponse>(
          `/api/tenants/${tenantId}/integrations/${integrationId}/credentials`,
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

export interface SetCredentialsResult {
  status: 'success' | 'error'
  errorCode?: SetCredentialsErrorCode
}

/**
 * RF-006.2/RF-006.4/RF-006.5: cadastro/alteração de credenciais iService.
 *
 * PUT /api/tenants/{tenantId}/integrations/{integrationId}/credentials
 *
 * Idempotente: cria ou substitui. O backend sempre reseta validationStatus
 * para NotValidated ao alterar (RN-006.3). O segredo enviado neste
 * formulário nunca é reexibido, mantido em estado ou logado após o envio.
 */
export function useSetIServiceCredentials(tenantId: string | null, integrationId: string | null) {
  const [isSubmitting, setIsSubmitting] = useState(false)

  const setCredentials = useCallback(
    async (payload: SetIServiceCredentialsRequest): Promise<SetCredentialsResult> => {
      if (!tenantId || !integrationId) {
        return { status: 'error', errorCode: 'integration_not_found' }
      }

      setIsSubmitting(true)
      try {
        await apiClient.put(
          `/api/tenants/${tenantId}/integrations/${integrationId}/credentials`,
          payload,
        )
        return { status: 'success' }
      } catch (err) {
        if (err instanceof ApiError) {
          if (err.status === 403) {
            return { status: 'error', errorCode: 'forbidden' }
          }
          return {
            status: 'error',
            errorCode: (err.code as SetCredentialsErrorCode) ?? 'unknown_error',
          }
        }
        return { status: 'error', errorCode: 'unknown_error' }
      } finally {
        setIsSubmitting(false)
      }
    },
    [tenantId, integrationId],
  )

  return { setCredentials, isSubmitting }
}

export interface ValidateCredentialsResult {
  status: 'success' | 'error'
  validationStatus?: 'Succeeded' | 'Failed'
  evaluatedAtUtc?: string
  errorCode?: ValidateCredentialsErrorCode
}

/**
 * RF-007: teste de validação de credenciais (não persiste sessão de
 * trabalho, apenas registra sucesso/falha + timestamp).
 *
 * POST /api/tenants/{tenantId}/integrations/{integrationId}/credentials/validate
 */
export function useValidateIServiceCredentials(
  tenantId: string | null,
  integrationId: string | null,
) {
  const [isValidating, setIsValidating] = useState(false)

  const validate = useCallback(async (): Promise<ValidateCredentialsResult> => {
    if (!tenantId || !integrationId) {
      return { status: 'error', errorCode: 'credentials_not_configured' }
    }

    setIsValidating(true)
    try {
      const response = await apiClient.post<ValidateIServiceCredentialsResponse>(
        `/api/tenants/${tenantId}/integrations/${integrationId}/credentials/validate`,
      )
      return {
        status: 'success',
        validationStatus: response?.validationStatus as 'Succeeded' | 'Failed',
        evaluatedAtUtc: response?.evaluatedAtUtc,
      }
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 403) {
          return { status: 'error', errorCode: 'forbidden' }
        }
        return {
          status: 'error',
          errorCode: (err.code as ValidateCredentialsErrorCode) ?? 'unknown_error',
        }
      }
      return { status: 'error', errorCode: 'unknown_error' }
    } finally {
      setIsValidating(false)
    }
  }, [tenantId, integrationId])

  return { validate, isValidating }
}
