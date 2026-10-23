import { useEffect, useRef, useState } from 'react'
import { apiClient } from '../../../shared/lib/apiClient'
import type {
  TriagemListScope,
  TriagemListStatus,
  TriagemPoolListItem,
  TriagemPoolListResponse,
} from '../../../shared/types/triagePool'

/** Intervalo de polling do pool (decisão do PO, seção 5 do épico): 30 s. */
export const TRIAGE_POOL_POLL_INTERVAL_MS = 30_000

/**
 * C1 — lista do pool compartilhado de triagem do tenant ativo, com
 * atualização por polling a cada 30 s (push em tempo real é incremento
 * futuro — ver épico, seção 2).
 *
 * GET /api/tenants/{tenantId}/triage-pool?status={estado}&scope={escopo}
 *
 * `scope` = `pool` (padrão, omitido na URL) ou `in-progress` (triagens
 * ainda em coleta, pré-pool — somente leitura no Office).
 *
 * `tenantId` deve vir do tenant ativo do usuário (useMyTenants().defaultTenantId).
 * Enquanto `tenantId` for `null`, nenhuma requisição é disparada.
 */
export function useTriagePoolList(
  tenantId: string | null,
  statusFilter?: TriagemListStatus,
  scope: TriagemListScope = 'pool',
) {
  const [items, setItems] = useState<TriagemPoolListItem[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [isError, setIsError] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)
  const isFirstLoadRef = useRef(true)

  useEffect(() => {
    isFirstLoadRef.current = true
  }, [tenantId, statusFilter, scope])

  useEffect(() => {
    let isCancelled = false

    async function load() {
      if (!tenantId) {
        if (!isCancelled) {
          setIsLoading(false)
          setItems([])
          setTotalCount(0)
        }
        return
      }

      // Evita "piscar" o estado de carregamento a cada poll; só mostra o
      // loading inicial (ou após troca de tenant/filtro).
      if (isFirstLoadRef.current) setIsLoading(true)
      setIsError(false)

      try {
        const params = new URLSearchParams()
        if (statusFilter) params.set('status', statusFilter)
        if (scope !== 'pool') params.set('scope', scope)
        const queryString = params.toString()
        const query = queryString ? `?${queryString}` : ''
        const response = await apiClient.get<TriagemPoolListResponse>(
          `/api/tenants/${tenantId}/triage-pool${query}`,
        )
        if (isCancelled) return
        setItems(response?.items ?? [])
        setTotalCount(response?.totalCount ?? 0)
      } catch {
        if (isCancelled) return
        setIsError(true)
        setItems([])
        setTotalCount(0)
      } finally {
        if (!isCancelled) {
          setIsLoading(false)
          isFirstLoadRef.current = false
        }
      }
    }

    void load()
    const intervalId = setInterval(() => void load(), TRIAGE_POOL_POLL_INTERVAL_MS)

    return () => {
      isCancelled = true
      clearInterval(intervalId)
    }
  }, [tenantId, statusFilter, scope, reloadToken])

  const refetch = () => setReloadToken((token) => token + 1)

  return { items, totalCount, isLoading, isError, refetch }
}
