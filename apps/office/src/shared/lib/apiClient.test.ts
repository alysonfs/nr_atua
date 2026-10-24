import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiClient, setAccessTokenProvider, setSessionRefreshHandler } from './apiClient'

const fetchMock = vi.fn()

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
  setAccessTokenProvider(() => 'synthetic-test-token')
  setSessionRefreshHandler(null)
})

afterEach(() => {
  setAccessTokenProvider(() => null)
  setSessionRefreshHandler(null)
  vi.unstubAllGlobals()
})

describe('apiClient binary content', () => {
  it('reads bytes using the session authorization and cancellation signal', async () => {
    const bytes = new Uint8Array([37, 80, 68, 70])
    fetchMock.mockResolvedValue(new Response(bytes))
    const controller = new AbortController()
    const data = await apiClient.getArrayBuffer('/api/attachment', controller.signal)
    expect(new Uint8Array(data!)).toEqual(bytes)
    const options = fetchMock.mock.calls[0][1] as RequestInit
    expect(new Headers(options.headers).get('Authorization')).toBe('Bearer synthetic-test-token')
    expect(options.signal).toBe(controller.signal)
  })

  it('keeps binary response handling after renewing an expired session', async () => {
    const refresh = vi.fn().mockResolvedValue(true)
    setSessionRefreshHandler(refresh)
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(new Uint8Array([1, 2, 3])))
    expect(new Uint8Array((await apiClient.getArrayBuffer('/api/attachment'))!)).toEqual(new Uint8Array([1, 2, 3]))
    expect(refresh).toHaveBeenCalledOnce()
  })

  it('reports access errors instead of returning an empty document', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ error: 'attachment_not_found' }), {
      status: 404,
      headers: { 'content-type': 'application/json' },
    }))
    await expect(apiClient.getArrayBuffer('/api/attachment')).rejects.toMatchObject<ApiError>({
      name: 'ApiError', status: 404, code: 'attachment_not_found', message: 'attachment_not_found',
      body: { error: 'attachment_not_found' },
    })
  })
})
