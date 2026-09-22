/**
 * Test suite for useWorkOrderDetail.
 *
 * Cobre:
 * - Chamada ao endpoint correto com tenantId/workOrderId.
 * - Sucesso: retorna o detalhe da OS.
 * - Erro genérico (rede/servidor).
 * - 404: distinguido do erro genérico via `notFound`.
 * - Nenhuma requisição é disparada enquanto tenantId ou workOrderId forem null.
 */

import { renderHook, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useWorkOrderDetail } from '../../hooks/useWorkOrderDetail'

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
const WORK_ORDER_ID = 'os-1'

const mockedDetail = {
  id: WORK_ORDER_ID,
  workOrderProviderId: 'EXT-1001',
  workOrderProviderNo: 'BRWO260909869',
  status: 'Designado',
  createdAt: '2026-09-01T10:00:00Z',
  updatedAt: '2026-09-02T10:00:00Z',
  providerCreatedAt: '2026-09-01T09:00:00Z',
  providerUpdatedAt: '2026-09-02T09:00:00Z',
  serviceRequestId: 'SR-1',
  amount: 150.5,
  customerType: 'Pessoa Física',
  customerName: 'Maria Souza',
  customerCpf: '123.456.789-00',
  contactEmail: 'maria@example.com',
  contactPhone: '84999999999',
  contactName: 'Maria Souza',
  address: 'Rua das Flores, 123',
  zipCode: '59000-000',
  countryName: 'Brasil',
  stateName: 'RN',
  cityName: 'Natal',
  productBrand: 'Consul',
  pdCode: 'PD-1',
  categoryId: 'cat-1',
  productCategoryCode: 'REFR',
  productCode: 'PC-1',
  productModel: 'CRM43',
  productStatus: 'Em análise',
  symptom: 'Não gela',
  history: [
    { status: 'Novo', createdAt: '2026-09-01T10:00:00Z' },
    { status: 'Designado', createdAt: '2026-09-02T10:00:00Z' },
  ],
}

describe('useWorkOrderDetail', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('requests the work order detail endpoint with tenant and work order ids', async () => {
    getMock.mockResolvedValueOnce(mockedDetail)

    const { result } = renderHook(() => useWorkOrderDetail(TENANT_ID, WORK_ORDER_ID))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).toHaveBeenCalledWith(`/api/tenants/${TENANT_ID}/work-orders/${WORK_ORDER_ID}`)
  })

  it('returns the detail from the API response on success', async () => {
    getMock.mockResolvedValueOnce(mockedDetail)

    const { result } = renderHook(() => useWorkOrderDetail(TENANT_ID, WORK_ORDER_ID))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.detail).toEqual(mockedDetail)
    expect(result.current.isError).toBe(false)
    expect(result.current.notFound).toBe(false)
  })

  it('sets isError on a generic request failure', async () => {
    getMock.mockRejectedValueOnce(new Error('network error'))

    const { result } = renderHook(() => useWorkOrderDetail(TENANT_ID, WORK_ORDER_ID))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
    expect(result.current.notFound).toBe(false)
    expect(result.current.detail).toBeNull()
  })

  it('sets notFound (not isError) on a 404 response', async () => {
    const { ApiError } = await import('../../../../shared/lib/apiClient')
    getMock.mockRejectedValueOnce(new ApiError(404, 'not_found', 'Not found'))

    const { result } = renderHook(() => useWorkOrderDetail(TENANT_ID, WORK_ORDER_ID))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.notFound).toBe(true)
    expect(result.current.isError).toBe(false)
    expect(result.current.detail).toBeNull()
  })

  it('does not request anything while tenantId or workOrderId are null', async () => {
    const { result } = renderHook(() => useWorkOrderDetail(null, WORK_ORDER_ID))

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(getMock).not.toHaveBeenCalled()
    expect(result.current.detail).toBeNull()
  })
})
