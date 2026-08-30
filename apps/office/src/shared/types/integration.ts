/**
 * RF-006/RF-007: Credenciais iService e validação
 *
 * Contratos (ADR-018):
 * PUT  /api/tenants/{tenantId}/integrations/{integrationId}/credentials
 * GET  /api/tenants/{tenantId}/integrations/{integrationId}/credentials
 * POST /api/tenants/{tenantId}/integrations/{integrationId}/credentials/validate
 *
 * Importante: o segredo (usuário/senha do iService) nunca é lido de volta
 * da API após o envio do formulário. Apenas o status de validação é exposto.
 */
export type EIServiceValidationStatus = 'NotValidated' | 'Succeeded' | 'Failed'

export interface SetIServiceCredentialsRequest {
  username: string
  password: string
  baseUrl?: string
}

export interface GetIServiceCredentialsResponse {
  hasCredentials: boolean
  validationStatus: EIServiceValidationStatus
  lastValidatedAtUtc: string | null
  updatedAtUtc: string | null
}

export interface ValidateIServiceCredentialsResponse {
  validationStatus: EIServiceValidationStatus
  evaluatedAtUtc: string
}

export type SetCredentialsErrorCode =
  | 'integration_not_found'
  | 'invalid_credentials'
  | 'forbidden'
  | 'unknown_error'

export type ValidateCredentialsErrorCode =
  | 'credentials_not_configured'
  | 'forbidden'
  | 'unknown_error'
