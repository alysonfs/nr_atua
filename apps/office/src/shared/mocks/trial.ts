import type { TrialDTO } from '../types/trial'

/**
 * Mock data for Trial - used during development
 * until backend endpoint GET /api/users/me/trial is available
 */
export const mockTrialData: TrialDTO = {
  trialId: 'trial-dev-123',
  expiresAtUtc: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
  activatedAtUtc: new Date().toISOString(),
  status: 'active',
  daysRemaining: 7,
}

/**
 * Mock Trial data - expired (for testing UI state)
 */
export const mockTrialDataExpired: TrialDTO = {
  trialId: 'trial-dev-expired',
  expiresAtUtc: new Date(Date.now() - 1 * 24 * 60 * 60 * 1000).toISOString(),
  activatedAtUtc: new Date(Date.now() - 8 * 24 * 60 * 60 * 1000).toISOString(),
  status: 'expired',
  daysRemaining: 0,
}

/**
 * Mock Trial data - about to expire (< 2 days)
 */
export const mockTrialDataExpiring: TrialDTO = {
  trialId: 'trial-dev-expiring',
  expiresAtUtc: new Date(Date.now() + 1.5 * 24 * 60 * 60 * 1000).toISOString(),
  activatedAtUtc: new Date(Date.now() - 5.5 * 24 * 60 * 60 * 1000).toISOString(),
  status: 'active',
  daysRemaining: 1,
}

/**
 * Mock Trial data - urgent (< 4 hours)
 */
export const mockTrialDataUrgent: TrialDTO = {
  trialId: 'trial-dev-urgent',
  expiresAtUtc: new Date(Date.now() + 2 * 60 * 60 * 1000).toISOString(),
  activatedAtUtc: new Date(Date.now() - 6.9 * 24 * 60 * 60 * 1000).toISOString(),
  status: 'active',
  daysRemaining: 0,
}
