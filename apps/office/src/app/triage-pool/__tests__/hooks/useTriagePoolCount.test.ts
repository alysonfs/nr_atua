/**
 * Test suite for useTriagePoolCount (contador do pool/em-triagem na
 * navegação).
 *
 * Cobre:
 * - Requisição única com `scope=all` (pool + em-triagem/pré-pool).
 * - `count` refletindo o `totalCount` retornado pela API.
 * - `isError` propagado da lista.
 * - Nenhuma requisição é disparada enquanto o tenant ainda não foi resolvido.
 */

import { renderHook, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useTriagePoolCount } from '../../hooks/useTriagePoolCount'

const getMock = vi.fn()
const useMyTenantsMock = vi.fn()

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

vi.mock('../../../office/hooks/useTenants', () => ({
  useMyTenants: () => useMyTenantsMock(),
}))

const TENANT_ID = 'tenant-1'

describe('useTriagePoolCount', () => {
  beforeEach(() => {
    getMock.mockReset()
    useMyTenantsMock.mockReset()
    useMyTenantsMock.mockReturnValue({ defaultTenantId: TENANT_ID })
  })

  it('requests the pool list with scope=all (pool + in-progress) in a single request', async () => {
    getMock.mockResolvedValueOnce({ totalCount: 3, items: [] })

    const { result } = renderHook(() => useTriagePoolCount())

    await waitFor(() => expect(result.current.count).toBe(3))

    expect(getMock).toHaveBeenCalledTimes(1)
    expect(getMock).toHaveBeenCalledWith(`/api/tenants/${TENANT_ID}/triage-pool?scope=all`)
  })

  it('propagates isError from the underlying list request', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    const { result } = renderHook(() => useTriagePoolCount())

    await waitFor(() => expect(result.current.isError).toBe(true))

    expect(result.current.count).toBe(0)
  })

  it('does not request anything while the tenant is not yet resolved', async () => {
    useMyTenantsMock.mockReturnValue({ defaultTenantId: null })

    const { result } = renderHook(() => useTriagePoolCount())

    await waitFor(() => expect(result.current.count).toBe(0))

    expect(getMock).not.toHaveBeenCalled()
  })
})
