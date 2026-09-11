/**
 * RF-017: status de OS é uma string livre (sem enum fixo). Este hook expõe
 * as ordens de serviço atualmente com status "Designado" para exibição na
 * tabela do Dashboard.
 *
 * Os dados retornados são MOCKADOS de forma determinística (sem
 * Math.random e sem `new Date()` variável), inspirados no modelo real de
 * domínio (`WorkOrder`: TenantId, ProviderId, Status, CreatedAt, UpdatedAt).
 * Quando o endpoint real de listagem de OS existir, este hook deve ser
 * substituído por uma integração real, mantendo o mesmo formato de
 * retorno (etapa futura).
 */

export interface DesignatedServiceOrder {
  /** Identificador da OS. */
  id: string
  /** Identificador do provedor responsável pela OS. */
  providerId: string
  /** Nome plausível do provedor, apenas para exibição. */
  providerName: string
  /** Status cru da OS (RF-017). Nesta tabela, sempre "Designado". */
  status: string
  /** Data/hora de criação da OS (ISO 8601). */
  createdAt: string
  /** Data/hora da última atualização da OS (ISO 8601). */
  updatedAt: string
}

const DESIGNATED_SERVICE_ORDERS: DesignatedServiceOrder[] = [
  {
    id: 'OS-1001',
    providerId: 'PRV-204',
    providerName: 'Refrigeração Natal Ltda',
    status: 'Designado',
    createdAt: '2026-09-01T09:15:00.000Z',
    updatedAt: '2026-09-02T13:40:00.000Z',
  },
  {
    id: 'OS-1002',
    providerId: 'PRV-118',
    providerName: 'Frio Certo Serviços',
    status: 'Designado',
    createdAt: '2026-09-02T11:05:00.000Z',
    updatedAt: '2026-09-03T08:20:00.000Z',
  },
  {
    id: 'OS-1003',
    providerId: 'PRV-052',
    providerName: 'Climatec Manutenção',
    status: 'Designado',
    createdAt: '2026-09-03T14:30:00.000Z',
    updatedAt: '2026-09-04T10:00:00.000Z',
  },
  {
    id: 'OS-1004',
    providerId: 'PRV-204',
    providerName: 'Refrigeração Natal Ltda',
    status: 'Designado',
    createdAt: '2026-09-05T16:45:00.000Z',
    updatedAt: '2026-09-06T09:10:00.000Z',
  },
  {
    id: 'OS-1005',
    providerId: 'PRV-311',
    providerName: 'Gelo Norte Assistência',
    status: 'Designado',
    createdAt: '2026-09-06T08:00:00.000Z',
    updatedAt: '2026-09-07T15:25:00.000Z',
  },
]

/**
 * Retorna a lista mockada de OS com status "Designado".
 */
export function useDesignatedServiceOrders(): DesignatedServiceOrder[] {
  return DESIGNATED_SERVICE_ORDERS
}
