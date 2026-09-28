/**
 * Test suite for useWorkOrderNextAppointmentSummary.
 *
 * Cobre:
 * - Chamada ao endpoint correto (RF-030).
 * - Parsing do shape real da API (overdue/dueToday/dueTomorrow).
 * - Estado de erro em caso de falha de rede.
 * - Nenhuma requisição é disparada enquanto tenantId for null.
 */

import { renderHook, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useWorkOrderNextAppointmentSummary } from '../../hooks/useWorkOrderNextAppointmentSummary'

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

describe('useWorkOrderNextAppointmentSummary', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('requests the next-appointment-summary endpoint with tenant id', async () => {
    getMock.mockResolvedValueOnce({ overdue: 0, dueToday: 0, dueTomorrow: 0 })

    const { result } = renderHook(() => useWorkOrderNextAppointmentSummary('tenant-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).toHaveBeenCalledWith('/api/tenants/tenant-1/work-orders/next-appointment-summary')
  })

  it('returns the summary from the API response', async () => {
    const summary = { overdue: 12, dueToday: 3, dueTomorrow: 5 }
    getMock.mockResolvedValueOnce(summary)

    const { result } = renderHook(() => useWorkOrderNextAppointmentSummary('tenant-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.summary).toEqual(summary)
    expect(result.current.isError).toBe(false)
  })

  it('sets isError and clears summary on request failure', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    const { result } = renderHook(() => useWorkOrderNextAppointmentSummary('tenant-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
    expect(result.current.summary).toBeNull()
  })

  it('does not request anything while tenantId is null', async () => {
    const { result } = renderHook(() => useWorkOrderNextAppointmentSummary(null))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).not.toHaveBeenCalled()
    expect(result.current.summary).toBeNull()
  })
})
