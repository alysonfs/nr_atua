/**
 * Test suite for useAssumeTriagem (C3 — assumir).
 *
 * Cobre:
 * - Chamada ao endpoint correto.
 * - Sucesso: retorna a resposta da API.
 * - 409 (concorrência): retorna o conflito com quem assumiu e quando.
 * - 404: triagem não encontrada.
 * - Erro genérico de rede.
 * - Nenhuma requisição enquanto tenantId for null.
 */

import { renderHook, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useAssumeTriagem } from '../../hooks/useAssumeTriagem'

const postMock = vi.fn()

vi.mock('../../../../shared/lib/apiClient', async () => {
  const actual =
    await vi.importActual<typeof import('../../../../shared/lib/apiClient')>(
      '../../../../shared/lib/apiClient',
    )
  return {
    ...actual,
    apiClient: {
      get: vi.fn(),
      post: (...args: unknown[]) => postMock(...args),
      put: vi.fn(),
    },
  }
})

const TENANT_ID = 'tenant-1'
const TRIAGEM_ID = 'triagem-1'

describe('useAssumeTriagem', () => {
  beforeEach(() => {
    postMock.mockReset()
  })

  it('requests the assume endpoint with tenant and triagem ids', async () => {
    postMock.mockResolvedValueOnce({
      triagemId: TRIAGEM_ID,
      status: 'AssumidaPorOperador',
      assumidaPorUserId: 'user-1',
      assumidaEmUtc: '2026-10-22T10:00:00Z',
    })

    const { result } = renderHook(() => useAssumeTriagem(TENANT_ID))

    await result.current.assume(TRIAGEM_ID)

    expect(postMock).toHaveBeenCalledWith(`/api/tenants/${TENANT_ID}/triage-pool/${TRIAGEM_ID}/assume`)
  })

  it('returns success with the response on 200', async () => {
    const response = {
      triagemId: TRIAGEM_ID,
      status: 'AssumidaPorOperador',
      assumidaPorUserId: 'user-1',
      assumidaEmUtc: '2026-10-22T10:00:00Z',
    }
    postMock.mockResolvedValueOnce(response)

    const { result } = renderHook(() => useAssumeTriagem(TENANT_ID))

    const outcome = await result.current.assume(TRIAGEM_ID)

    expect(outcome).toEqual({ status: 'success', response })
  })

  it('returns conflict with who assumed and when on 409', async () => {
    const { ApiError } = await import('../../../../shared/lib/apiClient')
    const conflictBody = {
      error: 'triagem_already_assumed',
      status: 'AssumidaPorOperador',
      assumedByUserId: 'user-2',
      assumedAtUtc: '2026-10-22T09:00:00Z',
    }
    postMock.mockRejectedValueOnce(new ApiError(409, 'triagem_already_assumed', 'Conflict', conflictBody))

    const { result } = renderHook(() => useAssumeTriagem(TENANT_ID))

    const outcome = await result.current.assume(TRIAGEM_ID)

    expect(outcome).toEqual({ status: 'conflict', conflict: conflictBody })
  })

  it('returns notFound on 404', async () => {
    const { ApiError } = await import('../../../../shared/lib/apiClient')
    postMock.mockRejectedValueOnce(new ApiError(404, 'triagem_not_found', 'Not found'))

    const { result } = renderHook(() => useAssumeTriagem(TENANT_ID))

    const outcome = await result.current.assume(TRIAGEM_ID)

    expect(outcome).toEqual({ status: 'notFound' })
  })

  it('returns a generic error on network failure', async () => {
    postMock.mockRejectedValueOnce(new Error('network error'))

    const { result } = renderHook(() => useAssumeTriagem(TENANT_ID))

    const outcome = await result.current.assume(TRIAGEM_ID)

    expect(outcome).toEqual({ status: 'error' })
  })

  it('returns a generic error without requesting anything while tenantId is null', async () => {
    const { result } = renderHook(() => useAssumeTriagem(null))

    const outcome = await result.current.assume(TRIAGEM_ID)

    expect(outcome).toEqual({ status: 'error' })
    expect(postMock).not.toHaveBeenCalled()
  })

  it('toggles isSubmitting while the request is in flight', async () => {
    let resolveRequest!: (value: unknown) => void
    postMock.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveRequest = resolve
      }),
    )

    const { result } = renderHook(() => useAssumeTriagem(TENANT_ID))

    const promise = result.current.assume(TRIAGEM_ID)
    await waitFor(() => expect(result.current.isSubmitting).toBe(true))

    resolveRequest({
      triagemId: TRIAGEM_ID,
      status: 'AssumidaPorOperador',
      assumidaPorUserId: 'user-1',
      assumidaEmUtc: '2026-10-22T10:00:00Z',
    })
    await promise

    await waitFor(() => expect(result.current.isSubmitting).toBe(false))
  })
})
