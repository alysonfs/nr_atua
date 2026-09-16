/**
 * RF-005: Acesso ao Office - tenants do usuário
 *
 * Contrato: GET /api/users/me/tenants (ADR-018)
 */
export interface TenantMembershipDTO {
  tenantId: string
  name: string
  role: 'OWNER' | 'ADMIN' | string
  integrationId: string
}

export interface GetMyTenantsResponse {
  tenants: TenantMembershipDTO[]
  defaultTenantId: string | null
}

/**
 * RF-006.1: Criação de tenant (nome + CNPJ)
 *
 * Contrato: POST /api/tenants (ADR-018)
 */
export interface CreateTenantRequest {
  name: string
  cnpj: string
}

export interface CreateTenantResponse {
  tenantId: string
  integrationId: string
}

/**
 * Erros conhecidos do contrato de criação de tenant.
 */
export type CreateTenantErrorCode =
  | 'invalid_cnpj'
  | 'cnpj_already_registered'
  | 'user_already_has_tenant'
  | 'trial_not_found'
  | 'unknown_error'
