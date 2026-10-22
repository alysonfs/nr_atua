import { useEffect, useRef, useState } from 'react'
import { ApiError, apiClient } from '../../../shared/lib/apiClient'
import type {
  WhatsAppSessionErrorCode,
  WhatsAppSessionStateResponse,
} from '../../../shared/types/whatsAppSession'

/** RF (ADR-049): o QR expira ~60s após o reporte do Channel; o polling do
 * Office precisa ser mais curto que isso para capturar a renovação. */
const POLL_INTERVAL_MS = 3_000

function mapError(error: unknown): WhatsAppSessionErrorCode {
  if (error instanceof ApiError) {
    if (error.status === 403) return 'forbidden'
    return (error.code as WhatsAppSessionErrorCode | undefined) ?? 'unknown_error'
  }
  return 'unknown_error'
}

/**
 * RF (ADR-049): consulta o último estado de pareamento WhatsApp do tenant e
 * mantém polling a cada 3s enquanto a página estiver aberta — o QR exibido
 * precisa acompanhar as renovações feitas pelo Channel (Baileys).
 *
 * Nunca loga o conteúdo do QR (nem em erro, nem em debug).
 */
export function useWhatsAppSession(tenantId: string | null, integrationId: string | null) {
  const [session, setSession] = useState<WhatsAppSessionStateResponse | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [errorCode, setErrorCode] = useState<WhatsAppSessionErrorCode | null>(null)
  const [reloadToken, setReloadToken] = useState(0)
  const hasLoadedOnceRef = useRef(false)

  useEffect(() => {
    hasLoadedOnceRef.current = false
  }, [tenantId, integrationId])

  useEffect(() => {
    if (!tenantId) {
      setIsLoading(false)
      setSession(null)
      setErrorCode(null)
      return
    }

    let isCancelled = false
    let timeoutId: ReturnType<typeof setTimeout> | undefined

    const path = integrationId
      ? `/api/tenants/${tenantId}/whatsapp-session?integrationId=${encodeURIComponent(integrationId)}`
      : `/api/tenants/${tenantId}/whatsapp-session`

    const load = async () => {
      if (!hasLoadedOnceRef.current) setIsLoading(true)
      try {
        const result = await apiClient.get<WhatsAppSessionStateResponse>(path)
        if (isCancelled) return
        setSession(result)
        setErrorCode(null)
      } catch (error) {
        if (isCancelled) return
        setSession(null)
        setErrorCode(mapError(error))
      } finally {
        if (!isCancelled) {
          setIsLoading(false)
          hasLoadedOnceRef.current = true
          timeoutId = setTimeout(() => void load(), POLL_INTERVAL_MS)
        }
      }
    }

    void load()

    return () => {
      isCancelled = true
      if (timeoutId) clearTimeout(timeoutId)
    }
  }, [tenantId, integrationId, reloadToken])

  const refetch = () => setReloadToken((token) => token + 1)

  return { session, isLoading, errorCode, refetch }
}
