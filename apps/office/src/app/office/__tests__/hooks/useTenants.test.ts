import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { useCreateTenant, useMyTenants } from '../../hooks/useTenants'
import { ApiError } from '../../../../shared/lib/apiClient'

const getMock = vi.fn()
const postMock = vi.fn()

vi.mock('../../../../shared/lib/apiClient', async () => {
  const actual =
    await vi.importActual<typeof import('../../../../shared/lib/apiClient')>(
      '../../../../shared/lib/apiClient',
    )
  return {
    ...actual,
    apiClient: {
      get: (...args: unknown[]) => getMock(...args),
      post: (...args: unknown[]) => postMock(...args),
      put: vi.fn(),
    },
  }
})

describe('useMyTenants', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('reflete usuário sem nenhum tenant (lista vazia, defaultTenantId null)', async () => {
    getMock.mockResolvedValueOnce({ tenants: [], defaultTenantId: null })

    const { result } = renderHook(() => useMyTenants())

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.hasNoTenant).toBe(true)
    expect(result.current.requiresSelection).toBe(false)
    expect(result.current.defaultTenantId).toBeNull()
  })

  it('seleciona automaticamente quando há exatamente um tenant', async () => {
    getMock.mockResolvedValueOnce({
      tenants: [
        { tenantId: 't1', name: 'Empresa A', role: 'OWNER', integrationId: 'int-1' },
      ],
      defaultTenantId: 't1',
    })

    const { result } = renderHook(() => useMyTenants())

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.hasNoTenant).toBe(false)
    expect(result.current.requiresSelection).toBe(false)
    expect(result.current.defaultTenantId).toBe('t1')
    expect(result.current.tenants[0].integrationId).toBe('int-1')
  })

  it('exige seleção explícita quando há múltiplos tenants', async () => {
    getMock.mockResolvedValueOnce({
      tenants: [
        { tenantId: 't1', name: 'Empresa A', role: 'OWNER', integrationId: 'int-1' },
        { tenantId: 't2', name: 'Empresa B', role: 'OWNER', integrationId: 'int-2' },
      ],
      defaultTenantId: null,
    })

    const { result } = renderHook(() => useMyTenants())

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.requiresSelection).toBe(true)
    expect(result.current.defaultTenantId).toBeNull()
  })

  it('trata falha de rede como erro, sem quebrar a UI', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    const { result } = renderHook(() => useMyTenants())

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
    expect(result.current.tenants).toEqual([])
  })
})

describe('useCreateTenant', () => {
  beforeEach(() => {
    postMock.mockReset()
  })

  it('retorna sucesso com tenantId e integrationId quando a API responde 201 (BUG-RF006-001)', async () => {
    postMock.mockResolvedValueOnce({ tenantId: 'tenant-123', integrationId: 'integration-456' })

    const { result } = renderHook(() => useCreateTenant())

    let creationResult
    await act(async () => {
      creationResult = await result.current.createTenant('Empresa', '11444777000161')
    })

    expect(creationResult).toEqual({
      status: 'success',
      tenantId: 'tenant-123',
      integrationId: 'integration-456',
    })
  })

  it('mapeia erro de CNPJ duplicado (409 cnpj_already_registered)', async () => {
    postMock.mockRejectedValueOnce(
      new ApiError(409, 'cnpj_already_registered', 'cnpj_already_registered'),
    )

    const { result } = renderHook(() => useCreateTenant())

    let creationResult
    await act(async () => {
      creationResult = await result.current.createTenant('Empresa', '11444777000161')
    })

    expect(creationResult).toEqual({ status: 'error', errorCode: 'cnpj_already_registered' })
  })
})
