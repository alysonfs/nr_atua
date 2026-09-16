/**
 * Test suite for useServiceOrderMonthSummary.
 *
 * Cobre:
 * - Chamada ao endpoint correto (tenant + mês formatado).
 * - Parsing do shape real da API (`{ month, timeZoneId, statuses }`).
 * - Estado de erro em caso de falha de rede.
 * - Nenhuma requisição é disparada enquanto tenantId for null.
 */

import { renderHook, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useServiceOrderMonthSummary } from '../../hooks/useServiceOrderMonthSummary'

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

describe('useServiceOrderMonthSummary', () => {
  const referenceDate = new Date(2026, 8, 10) // 10/09/2026

  beforeEach(() => {
    getMock.mockReset()
  })

  it('requests the summary endpoint with tenant id and formatted month', async () => {
    getMock.mockResolvedValueOnce({ month: '2026-09', timeZoneId: 'America/Sao_Paulo', statuses: [] })

    const { result } = renderHook(() => useServiceOrderMonthSummary('tenant-1', referenceDate))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).toHaveBeenCalledWith('/api/tenants/tenant-1/work-orders/summary?month=2026-09')
  })

  it('returns the statuses array from the API response', async () => {
    const statuses = [{ status: 'Designado', total: 5, dailyCounts: [1, 2, 3] }]
    getMock.mockResolvedValueOnce({ month: '2026-09', timeZoneId: 'America/Sao_Paulo', statuses })

    const { result } = renderHook(() => useServiceOrderMonthSummary('tenant-1', referenceDate))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.summary).toEqual(statuses)
    expect(result.current.isError).toBe(false)
  })

  it('sets isError and empties summary on request failure', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    const { result } = renderHook(() => useServiceOrderMonthSummary('tenant-1', referenceDate))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
    expect(result.current.summary).toEqual([])
  })

  it('does not request anything while tenantId is null', async () => {
    const { result } = renderHook(() => useServiceOrderMonthSummary(null, referenceDate))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).not.toHaveBeenCalled()
    expect(result.current.summary).toEqual([])
  })
})
