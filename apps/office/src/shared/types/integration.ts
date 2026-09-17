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

/**
 * RF-008 (ADR-020/ADR-024): estado de ativação do Agente Coletor.
 *
 * GET/PUT /api/tenants/{tenantId}/integrations/{integrationId}/collector-activation
 *
 * `canActivate` já reflete a elegibilidade do plano (ADR-024); a validação
 * de credenciais é apenas informativa e não bloqueia a ativação.
 */
export type ECollectorActivationStatus = 'Inactive' | 'Active'

export interface ImmediateCommandView {
  commandId: string
  status: string
  requestedAtUtc: string
}

export interface CollectorActivationView {
  status: ECollectorActivationStatus
  canActivate: boolean
  activationBlockReason: string
  credentialValidationStatus: EIServiceValidationStatus
  activatedAtUtc: string | null
  deactivatedAtUtc: string | null
  lastImmediateCommand: ImmediateCommandView | null
  lastSuccessfulCollectionAtUtc: string | null
}

export type ActivateCollectorErrorCode =
  | 'integration_not_found'
  | 'forbidden'
  | 'activation_not_eligible'
  | 'missing_idempotency_key'
  | 'idempotency_key_conflict'
  | 'unknown_error'

export type DeactivateCollectorErrorCode =
  | 'integration_not_found'
  | 'forbidden'
  | 'missing_idempotency_key'
  | 'idempotency_key_conflict'
  | 'unknown_error'

/**
 * RF-025: intervalo de coleta recorrente configurável (em minutos, 5-1440).
 *
 * GET/PUT /api/tenants/{tenantId}/integrations/{integrationId}/recurrent-collection-interval
 */
export interface RecurrentCollectionIntervalView {
  recurrentCollectionIntervalMinutes: number
}

export type RecurrentCollectionIntervalErrorCode =
  | 'integration_not_found'
  | 'invalid_recurrent_collection_interval'
  | 'forbidden'
  | 'unknown_error'
