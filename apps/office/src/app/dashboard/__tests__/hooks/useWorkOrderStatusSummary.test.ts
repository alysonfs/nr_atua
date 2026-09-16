/**
 * Test suite for useWorkOrderStatusSummary.
 *
 * Cobre:
 * - Chamada ao endpoint correto (contagem por status atual, sem mês).
 * - Parsing do shape real da API (`{ statuses }`).
 * - Estado de erro em caso de falha de rede.
 * - Nenhuma requisição é disparada enquanto tenantId for null.
 */

import { renderHook, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useWorkOrderStatusSummary } from '../../hooks/useWorkOrderStatusSummary'

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

describe('useWorkOrderStatusSummary', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('requests the status-summary endpoint with tenant id', async () => {
    getMock.mockResolvedValueOnce({ statuses: [] })

    const { result } = renderHook(() => useWorkOrderStatusSummary('tenant-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).toHaveBeenCalledWith('/api/tenants/tenant-1/work-orders/status-summary')
  })

  it('returns the statuses array from the API response', async () => {
    const statuses = [{ status: 'pending', total: 119 }]
    getMock.mockResolvedValueOnce({ statuses })

    const { result } = renderHook(() => useWorkOrderStatusSummary('tenant-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.summary).toEqual(statuses)
    expect(result.current.isError).toBe(false)
  })

  it('sets isError and empties summary on request failure', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    const { result } = renderHook(() => useWorkOrderStatusSummary('tenant-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
    expect(result.current.summary).toEqual([])
  })

  it('does not request anything while tenantId is null', async () => {
    const { result } = renderHook(() => useWorkOrderStatusSummary(null))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).not.toHaveBeenCalled()
    expect(result.current.summary).toEqual([])
  })
})
