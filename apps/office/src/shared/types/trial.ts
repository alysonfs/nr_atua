/**
 * RF-003: Trial status and information
 * 
 * The Trial is associated with the User and persists when a Tenant is created.
 * Status:
 * - 'active': Trial is ongoing (< 7 days from activation)
 * - 'expired': Trial period has ended
 * - 'converted': Trial has been converted to a paid plan
 */
export interface TrialDTO {
  trialId: string;
  expiresAtUtc: string; // ISO 8601 format
  activatedAtUtc: string; // ISO 8601 format
  status: 'active' | 'expired' | 'converted';
  daysRemaining: number;
}

/**
 * Response from GET /api/users/me/trial
 */
export type GetUserTrialResponse = TrialDTO;

/**
 * Trial status summary for UI display
 */
export interface TrialSummary {
  isActive: boolean;
  isExpired: boolean;
  daysRemaining: number;
  hoursRemaining: number;
  isAboutToExpire: boolean; // < 2 days remaining
  isUrgent: boolean; // < 4 hours remaining
}
