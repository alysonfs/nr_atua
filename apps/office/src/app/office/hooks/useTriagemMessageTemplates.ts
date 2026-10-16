import { useCallback, useEffect, useState } from 'react'
import { ApiError, apiClient } from '../../../shared/lib/apiClient'
import type {
  TriagemMessageTemplateErrorCode,
  TriagemMessageTemplatesResponse,
  TriagemMessageTemplateType,
} from '../../../shared/types/triagemMessageTemplate'

interface TemplateMutationResult {
  status: 'success' | 'error'
  response?: TriagemMessageTemplatesResponse
  errorCode?: TriagemMessageTemplateErrorCode
  validationErrors?: string[]
}

function mapError(error: unknown): TemplateMutationResult {
  if (error instanceof ApiError) {
    return {
      status: 'error',
      errorCode:
        error.status === 403
          ? 'forbidden'
          : ((error.code as TriagemMessageTemplateErrorCode | undefined) ?? 'validation_failed'),
    }

  }
  return { status: 'error', errorCode: 'unknown_error' }
}

function validateContext(
  tenantId: string | null,
  integrationId: string | null,
): TemplateMutationResult | null {
  return tenantId && integrationId
    ? null
    : { status: 'error', errorCode: 'integration_not_found' }
}

export function useTriagemMessageTemplates(tenantId: string | null, integrationId: string | null) {
  const [response, setResponse] = useState<TriagemMessageTemplatesResponse | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (!tenantId || !integrationId) {
        if (!isCancelled) {
          setResponse(null)
          setIsLoading(false)
        }
        return
      }

      setIsLoading(true)
      setIsError(false)
      try {
        const result = await apiClient.get<TriagemMessageTemplatesResponse>(
          `/api/tenants/${tenantId}/integrations/${integrationId}/triage-message-templates`,
        )
        if (!isCancelled) setResponse(result)
      } catch {
        if (!isCancelled) {
          setResponse(null)
          setIsError(true)
        }
      } finally {
        if (!isCancelled) setIsLoading(false)
      }
    })

    return () => {
      isCancelled = true
    }
  }, [tenantId, integrationId, reloadToken])

  const refetch = useCallback(() => setReloadToken((token) => token + 1), [])

  return { templates: response?.templates ?? [], isLoading, isError, refetch }
}

export function useTriagemMessageTemplateMutations(
  tenantId: string | null,
  integrationId: string | null,
) {
  const [isSaving, setIsSaving] = useState(false)
  const [isPublishing, setIsPublishing] = useState(false)
  const [isPreviewing, setIsPreviewing] = useState(false)

  const path = useCallback(
    (type: TriagemMessageTemplateType, operation: string) =>
      `/api/tenants/${tenantId}/integrations/${integrationId}/triage-message-templates/${type}/${operation}`,
    [tenantId, integrationId],
  )

  const saveDraft = useCallback(
    async (type: TriagemMessageTemplateType, content: string): Promise<TemplateMutationResult> => {
      const contextError = validateContext(tenantId, integrationId)
      if (contextError) return contextError
      setIsSaving(true)
      try {
        const result = await apiClient.put<TriagemMessageTemplatesResponse>(path(type, 'draft'), {
          content,
        })
        return { status: 'success', response: result ?? undefined }
      } catch (error) {
        return mapError(error)
      } finally {
        setIsSaving(false)
      }
    },
    [path, tenantId, integrationId],
  )

  const preview = useCallback(
    async (type: TriagemMessageTemplateType, content: string): Promise<TemplateMutationResult> => {
      const contextError = validateContext(tenantId, integrationId)
      if (contextError) return contextError
      setIsPreviewing(true)
      try {
        const result = await apiClient.post<TriagemMessageTemplatesResponse>(path(type, 'preview'), {
          content,
        })
        return { status: 'success', response: result ?? undefined }
      } catch (error) {
        return mapError(error)
      } finally {
        setIsPreviewing(false)
      }
    },
    [path, tenantId, integrationId],
  )

  const publish = useCallback(
    async (type: TriagemMessageTemplateType): Promise<TemplateMutationResult> => {
      const contextError = validateContext(tenantId, integrationId)
      if (contextError) return contextError
      setIsPublishing(true)
      try {
        const result = await apiClient.post<TriagemMessageTemplatesResponse>(path(type, 'publish'))
        return { status: 'success', response: result ?? undefined }
      } catch (error) {
        return mapError(error)
      } finally {
        setIsPublishing(false)
      }
    },
    [path, tenantId, integrationId],
  )

  return { saveDraft, preview, publish, isSaving, isPreviewing, isPublishing }
}
