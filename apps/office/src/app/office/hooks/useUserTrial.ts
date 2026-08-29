import { useMemo } from 'react'
import type { TrialDTO, TrialSummary } from '../../../shared/types/trial'
import { mockTrialData } from '../../../shared/mocks/trial'

/**
 * Hook to manage user trial status
 * 
 * Currently returns mock data. Will integrate with:
 * GET /api/users/me/trial
 * 
 * The Trial is associated with the User and persists when a Tenant is created.
 */
export function useUserTrial() {
  // TODO: Replace with actual API call when endpoint is available
  // For now, use mock data in development
  const isDevelopment = import.meta.env.DEV
  const trial: TrialDTO | null = isDevelopment ? mockTrialData : null

  const summary = useMemo(() => getTrialSummary(trial), [trial])

  return {
    trial,
    summary,
    isLoading: false, // TODO: Update when API integration is done
    isError: false,
    refetch: () => {
      // TODO: Implement refetch when API integration is done
    },
  }

}

export function getTrialSummary(trial: TrialDTO | null, nowUtc: Date = new Date()): TrialSummary | null {
  if (!trial) return null

  const expiresAt = new Date(trial.expiresAtUtc)
  const diffMs = expiresAt.getTime() - nowUtc.getTime()
  const isActive = nowUtc.getTime() < expiresAt.getTime()
  const daysRemaining = Math.max(0, Math.ceil(diffMs / (1000 * 60 * 60 * 24)))
  const hoursRemaining = Math.max(0, Math.ceil(diffMs / (1000 * 60 * 60)))

  return {
    isActive,
    isExpired: !isActive,
    daysRemaining,
    hoursRemaining,
    isAboutToExpire: isActive && daysRemaining < 2,
    isUrgent: isActive && hoursRemaining < 4,
  }
}

/**
 * Hook to check if trial is active
 */
export function useIsTrialActive(): boolean {
  const { summary } = useUserTrial()
  return summary?.isActive ?? false
}

/**
 * Hook to check if trial is about to expire (< 2 days)
 */
export function useIsTrialExpiring(): boolean {
  const { summary } = useUserTrial()
  return summary?.isAboutToExpire ?? false
}

/**
 * Hook to check if trial is urgent (< 4 hours)
 */
export function useIsTrialUrgent(): boolean {
  const { summary } = useUserTrial()
  return summary?.isUrgent ?? false
}

/**
 * Hook to get remaining days
 */
export function useTrialDaysRemaining(): number {
  const { summary } = useUserTrial()
  return summary?.daysRemaining ?? 0
}

/**
 * Hook to get remaining hours
 */
export function useTrialHoursRemaining(): number {
  const { summary } = useUserTrial()
  return summary?.hoursRemaining ?? 0
}
