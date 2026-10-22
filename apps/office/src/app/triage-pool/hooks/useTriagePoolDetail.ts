import { useCallback, useEffect, useState } from 'react'
import { ApiError, apiClient } from '../../../shared/lib/apiClient'
import type { TriagemPoolDetail } from '../../../shared/types/triagePool'

/**
 * C2 — detalhe de uma triagem do pool (ou já assumida pelo usuário/gestor).
 *
 * GET /api/tenants/{tenantId}/triage-pool/{triagemId}
 *
 * 404 é tratado separadamente (`notFound`) para exibir a mensagem amigável
 * "Triagem não encontrada ou sem acesso" (épico: triagem de outro tenant ou
 * assumida por outro operador não-gestor também retornam 404, por desenho).
 */
export function useTriagePoolDetail(tenantId: string | null, triagemId: string | null) {
  const [detail, setDetail] = useState<TriagemPoolDetail | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)
  const [notFound, setNotFound] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isCancelled = false

    void Promise.resolve().then(async () => {
      if (isCancelled) return

      if (!tenantId || !triagemId) {
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
        const response = await apiClient.get<TriagemPoolDetail>(
          `/api/tenants/${tenantId}/triage-pool/${triagemId}`,
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
  }, [tenantId, triagemId, reloadToken])

  const refetch = useCallback(() => setReloadToken((token) => token + 1), [])

  return { detail, isLoading, isError, notFound, refetch }
}
