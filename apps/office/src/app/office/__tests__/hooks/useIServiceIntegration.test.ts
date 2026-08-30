import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import {
  useIServiceCredentials,
  useSetIServiceCredentials,
  useValidateIServiceCredentials,
} from '../../hooks/useIServiceIntegration'
import { ApiError } from '../../../../shared/lib/apiClient'

const getMock = vi.fn()
const putMock = vi.fn()
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
      put: (...args: unknown[]) => putMock(...args),
      post: (...args: unknown[]) => postMock(...args),
    },
  }
})

describe('useIServiceCredentials', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('não busca status quando tenantId ou integrationId são nulos', async () => {
    const { result } = renderHook(() => useIServiceCredentials(null, null))

    await waitFor(() => expect(result.current.isLoading).toBe(false))
    expect(getMock).not.toHaveBeenCalled()
    expect(result.current.status).toBeNull()
  })

  it('nunca expõe segredo: retorna apenas validationStatus e timestamps', async () => {
    getMock.mockResolvedValueOnce({
      hasCredentials: true,
      validationStatus: 'Succeeded',
      lastValidatedAtUtc: '2026-08-29T00:00:00Z',
      updatedAtUtc: '2026-08-29T00:00:00Z',
    })

    const { result } = renderHook(() => useIServiceCredentials('t1', 'i1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(Object.keys(result.current.status ?? {})).toEqual([
      'hasCredentials',
      'validationStatus',
      'lastValidatedAtUtc',
      'updatedAtUtc',
    ])
  })
})

describe('useSetIServiceCredentials', () => {
  beforeEach(() => {
    putMock.mockReset()
  })

  it('mapeia 403 para erro de autorização (forbidden)', async () => {
    putMock.mockRejectedValueOnce(new ApiError(403, undefined, 'forbidden'))

    const { result } = renderHook(() => useSetIServiceCredentials('t1', 'i1'))

    let setResult
    await act(async () => {
      setResult = await result.current.setCredentials({ username: 'u', password: 'p' })
    })

    expect(setResult).toEqual({ status: 'error', errorCode: 'forbidden' })
  })

  it('retorna sucesso quando a API responde 204', async () => {
    putMock.mockResolvedValueOnce(null)

    const { result } = renderHook(() => useSetIServiceCredentials('t1', 'i1'))

    let setResult
    await act(async () => {
      setResult = await result.current.setCredentials({ username: 'u', password: 'p' })
    })

    expect(setResult).toEqual({ status: 'success' })
  })
})

describe('useValidateIServiceCredentials', () => {
  beforeEach(() => {
    postMock.mockReset()
  })

  it('mapeia 404 credentials_not_configured', async () => {
    postMock.mockRejectedValueOnce(
      new ApiError(404, 'credentials_not_configured', 'credentials_not_configured'),
    )

    const { result } = renderHook(() => useValidateIServiceCredentials('t1', 'i1'))

    let validationResult
    await act(async () => {
      validationResult = await result.current.validate()
    })

    expect(validationResult).toEqual({
      status: 'error',
      errorCode: 'credentials_not_configured',
    })
  })

  it('retorna validationStatus Failed sem detalhar a causa técnica', async () => {
    postMock.mockResolvedValueOnce({
      validationStatus: 'Failed',
      evaluatedAtUtc: '2026-08-29T01:00:00Z',
    })

    const { result } = renderHook(() => useValidateIServiceCredentials('t1', 'i1'))

    let validationResult
    await act(async () => {
      validationResult = await result.current.validate()
    })

    expect(validationResult).toEqual({
      status: 'success',
      validationStatus: 'Failed',
      evaluatedAtUtc: '2026-08-29T01:00:00Z',
    })
  })
})
