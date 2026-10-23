/**
 * Test suite for useAiUsageSummary.
 *
 * Cobre:
 * - Chamada ao endpoint correto com `from`/`to`.
 * - Sucesso: retorna o resumo.
 * - Erro genérico.
 */

import { renderHook, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useAiUsageSummary } from '../../hooks/useAiUsageSummary'

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

const mockedSummary = {
  from: '2026-09-24',
  to: '2026-10-23',
  currency: 'USD',
  totals: { calls: 10, inputTokens: 100, outputTokens: 50, estimatedCost: 1.2345, avgLatencyMs: 320, failedCalls: 1 },
  tenants: [],
  daily: [],
  models: [],
}

describe('useAiUsageSummary', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('requests the summary endpoint with from/to query parameters', async () => {
    getMock.mockResolvedValueOnce(mockedSummary)

    const { result } = renderHook(() => useAiUsageSummary('2026-09-24', '2026-10-23'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).toHaveBeenCalledWith('/api/llm-usage/summary?from=2026-09-24&to=2026-10-23')
  })

  it('returns the summary from the API response on success', async () => {
    getMock.mockResolvedValueOnce(mockedSummary)

    const { result } = renderHook(() => useAiUsageSummary('2026-09-24', '2026-10-23'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.summary).toEqual(mockedSummary)
    expect(result.current.isError).toBe(false)
  })

  it('sets isError on a request failure', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    const { result } = renderHook(() => useAiUsageSummary('2026-09-24', '2026-10-23'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
    expect(result.current.summary).toBeNull()
  })
})
