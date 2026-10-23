/**
 * Test suite for useTriagePoolList (C1 — lista do pool).
 *
 * Cobre:
 * - Chamada ao endpoint correto com tenantId.
 * - Filtro por estado aplicado via query string.
 * - Escopo `in-progress` (pré-pool) aplicado via query string.
 * - Sucesso: retorna items e totalCount.
 * - Erro genérico.
 * - Nenhuma requisição é disparada enquanto tenantId for null.
 * - Polling a cada 30 s (sem repetir o loading inicial).
 */

import { renderHook, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { useTriagePoolList } from '../../hooks/useTriagePoolList'

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

const TENANT_ID = 'tenant-1'

const mockedItem = {
  triagemId: 'triagem-1',
  workOrderProviderNo: 'BRWO260909869',
  customerName: 'Maria Souza',
  productModel: 'CRM43',
  productBrand: 'Consul',
  status: 'LiberadaAoPool',
  checklistItemsCompleted: 4,
  checklistItemsTotal: 5,
  enteredPoolAtUtc: '2026-10-20T10:00:00Z',
  hasNewActivity: false,
}

describe('useTriagePoolList', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('requests the pool list endpoint with the tenant id, without a status filter', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 0, items: [] })

    const { result } = renderHook(() => useTriagePoolList(TENANT_ID))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).toHaveBeenCalledWith(`/api/tenants/${TENANT_ID}/triage-pool`)
  })

  it('applies the status filter as a query parameter', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 0, items: [] })

    const { result } = renderHook(() => useTriagePoolList(TENANT_ID, 'Expirada'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).toHaveBeenCalledWith(`/api/tenants/${TENANT_ID}/triage-pool?status=Expirada`)
  })

  it('applies the in-progress scope (and status) as query parameters', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 0, items: [] })

    const { result } = renderHook(() => useTriagePoolList(TENANT_ID, 'EmColeta', 'in-progress'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).toHaveBeenCalledWith(`/api/tenants/${TENANT_ID}/triage-pool?status=EmColeta&scope=in-progress`)
  })

  it('returns the items and totalCount from the API response on success', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 1, items: [mockedItem] })

    const { result } = renderHook(() => useTriagePoolList(TENANT_ID))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.items).toEqual([mockedItem])
    expect(result.current.totalCount).toBe(1)
    expect(result.current.isError).toBe(false)
  })

  it('sets isError on a request failure', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    const { result } = renderHook(() => useTriagePoolList(TENANT_ID))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
    expect(result.current.items).toEqual([])
  })

  it('does not request anything while tenantId is null', async () => {
    const { result } = renderHook(() => useTriagePoolList(null))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).not.toHaveBeenCalled()
  })

  it('polls the endpoint again after 30 seconds', async () => {
    vi.useFakeTimers()
    getMock.mockResolvedValue({ totalCount: 1, items: [mockedItem] })

    renderHook(() => useTriagePoolList(TENANT_ID))

    await vi.waitFor(() => expect(getMock).toHaveBeenCalledTimes(1))

    await vi.advanceTimersByTimeAsync(30_000)

    expect(getMock).toHaveBeenCalledTimes(2)
  })
})
