/**
 * Test suite for useWorkOrderCriticalitySummary.
 *
 * Cobre:
 * - Chamada ao endpoint correto (RF-033).
 * - Parsing do shape real da API (critical/atRisk).
 * - Estado de erro em caso de falha de rede.
 * - Nenhuma requisição é disparada enquanto tenantId for null.
 */

import { renderHook, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useWorkOrderCriticalitySummary } from '../../hooks/useWorkOrderCriticalitySummary'

const getMock = vi.fn()

vi.mock('../../../../shared/lib/apiClient', async () => {
  const actual =
    await vi.importActual<typeof import('../../../../shared/lib/apiClient')>(
      '../../../../shared/lib/apiClient',
    )
  return {
    ...actual,
    apiClient: {
      get: (...args: unknown[]) => getMock(...args),
      post: vi.fn(),
      put: vi.fn(),
    },
  }
})

describe('useWorkOrderCriticalitySummary', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('requests the criticality-summary endpoint with tenant id', async () => {
    getMock.mockResolvedValueOnce({ critical: 0, atRisk: 0 })

    const { result } = renderHook(() => useWorkOrderCriticalitySummary('tenant-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).toHaveBeenCalledWith('/api/tenants/tenant-1/work-orders/criticality-summary')
  })

  it('returns the summary from the API response', async () => {
    const summary = { critical: 8, atRisk: 4 }
    getMock.mockResolvedValueOnce(summary)

    const { result } = renderHook(() => useWorkOrderCriticalitySummary('tenant-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.summary).toEqual(summary)
    expect(result.current.isError).toBe(false)
  })

  it('sets isError and clears summary on request failure', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    const { result } = renderHook(() => useWorkOrderCriticalitySummary('tenant-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
    expect(result.current.summary).toBeNull()
  })

  it('does not request anything while tenantId is null', async () => {
    const { result } = renderHook(() => useWorkOrderCriticalitySummary(null))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).not.toHaveBeenCalled()
    expect(result.current.summary).toBeNull()
  })
})
