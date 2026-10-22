/**
 * RF (ADR-049): pareamento do WhatsApp via QR a partir do Office.
 *
 * Contrato: GET /api/tenants/{tenantId}/whatsapp-session?integrationId=
 * (BrowserSession, restrito a OWNER/ADMIN do tenant).
 */
export type WhatsAppSessionState =
  | 'unknown'
  | 'qr_pending'
  | 'connected'
  | 'disconnected'
  | 'logged_out'

export interface WhatsAppSessionStateResponse {
  state: WhatsAppSessionState
  qr?: string | null
  qrExpiresAtUtc?: string | null
  phoneNumber?: string | null
  updatedAtUtc?: string | null
}

/** Erros conhecidos do contrato de consulta de sessão WhatsApp. */
export type WhatsAppSessionErrorCode =
  | 'integration_id_required'
  | 'integration_not_found'
  | 'forbidden'
  | 'unknown_error'
