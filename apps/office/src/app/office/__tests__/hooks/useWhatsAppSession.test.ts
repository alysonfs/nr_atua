import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useWhatsAppSession } from '../../hooks/useWhatsAppSession'
import { ApiError } from '../../../../shared/lib/apiClient'

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
    },
  }
})

describe('useWhatsAppSession', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('não consulta quando tenantId é nulo', async () => {
    const { result } = renderHook(() => useWhatsAppSession(null, 'integration-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))
    expect(getMock).not.toHaveBeenCalled()
    expect(result.current.session).toBeNull()
  })

  it('inclui integrationId na query string quando informado', async () => {
    getMock.mockResolvedValue({ state: 'unknown' })

    const { result } = renderHook(() => useWhatsAppSession('tenant-1', 'integration-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).toHaveBeenCalledWith(
      '/api/tenants/tenant-1/whatsapp-session?integrationId=integration-1',
    )
  })

  it('repete a consulta a cada 3s (polling)', async () => {
    vi.useFakeTimers()
    getMock.mockResolvedValue({ state: 'qr_pending', qr: 'raw-qr' })

    renderHook(() => useWhatsAppSession('tenant-1', 'integration-1'))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(getMock).toHaveBeenCalledTimes(1)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3_000)
    })
    expect(getMock).toHaveBeenCalledTimes(2)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3_000)
    })
    expect(getMock).toHaveBeenCalledTimes(3)
  })

  it('mapeia 400 integration_id_required', async () => {
    getMock.mockRejectedValue(new ApiError(400, 'integration_id_required', 'integration_id_required'))

    const { result } = renderHook(() => useWhatsAppSession('tenant-1', null))

    await waitFor(() => expect(result.current.isLoading).toBe(false))
    expect(result.current.errorCode).toBe('integration_id_required')
    expect(result.current.session).toBeNull()
  })

  it('mapeia 403 forbidden', async () => {
    getMock.mockRejectedValue(new ApiError(403, undefined, 'forbidden'))

    const { result } = renderHook(() => useWhatsAppSession('tenant-1', 'integration-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))
    expect(result.current.errorCode).toBe('forbidden')
  })

  it('mapeia 404 integration_not_found', async () => {
    getMock.mockRejectedValue(new ApiError(404, 'integration_not_found', 'integration_not_found'))

    const { result } = renderHook(() => useWhatsAppSession('tenant-1', 'integration-1'))

    await waitFor(() => expect(result.current.isLoading).toBe(false))
    expect(result.current.errorCode).toBe('integration_not_found')
  })
})
