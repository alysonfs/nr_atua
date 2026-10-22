import { useCallback, useState } from 'react'
import { ApiError, apiClient } from '../../../shared/lib/apiClient'
import type { AssumeTriagemConflict, AssumeTriagemResponse } from '../../../shared/types/triagePool'

export type AssumeTriagemResult =
  | { status: 'success'; response: AssumeTriagemResponse }
  | { status: 'conflict'; conflict: AssumeTriagemConflict }
  | { status: 'notFound' }
  | { status: 'error' }

const UNKNOWN_CONFLICT: AssumeTriagemConflict = {
  error: 'triagem_already_assumed',
  status: null,
  assumedByUserId: null,
  assumedAtUtc: null,
}

/**
 * C3 — assumir uma triagem do pool, com exclusividade por concorrência
 * otimista (épico, critério de aceite 7): o primeiro operador a confirmar
 * vence; o segundo recebe 409 com quem assumiu e quando.
 *
 * POST /api/tenants/{tenantId}/triage-pool/{triagemId}/assume
 */
export function useAssumeTriagem(tenantId: string | null) {
  const [isSubmitting, setIsSubmitting] = useState(false)

  const assume = useCallback(
    async (triagemId: string): Promise<AssumeTriagemResult> => {
      if (!tenantId) return { status: 'error' }

      setIsSubmitting(true)
      try {
        const response = await apiClient.post<AssumeTriagemResponse>(
          `/api/tenants/${tenantId}/triage-pool/${triagemId}/assume`,
        )
        if (!response) return { status: 'error' }
        return { status: 'success', response }
      } catch (err) {
        if (err instanceof ApiError) {
          if (err.status === 409) {
            const conflict = (err.body as AssumeTriagemConflict | null) ?? UNKNOWN_CONFLICT
            return { status: 'conflict', conflict }
          }
          if (err.status === 404) return { status: 'notFound' }
        }
        return { status: 'error' }
      } finally {
        setIsSubmitting(false)
      }
    },
    [tenantId],
  )

  return { assume, isSubmitting }
}
