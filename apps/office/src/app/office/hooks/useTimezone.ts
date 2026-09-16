import { useCallback, useState } from 'react'
import {
  formatDateInTimezone,
  getSuggestedTimezone,
  isValidTimezone,
} from '../lib/timezone'
import { useTranslation } from 'react-i18next'

/**
 * Hook to manage user timezone preference
 * 
 * Currently returns browser/suggested timezone.
 * Will integrate with:
 * - GET /api/users/me/timezone (read current preference)
 * - PUT /api/users/me/timezone (update preference)
 * 
 * Timezone is stored in IANA format.
 * All backend dates are in UTC and are formatted to user's timezone in the UI.
 */
export function useTimezone() {
  const { t } = useTranslation()
  // TODO: Replace with actual API call when endpoint is available
  const [timezone, setTimezone] = useState<string>(() => {
    // Try to get from localStorage first
    const stored = localStorage.getItem('user_timezone')
    if (stored && isValidTimezone(stored)) {
      return stored
    }

    // Suggest timezone based on browser language
    const suggested = getSuggestedTimezone()
    return suggested
  })

  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  /**
   * Format a UTC date to the user's timezone
   */
  const formatDateLocal = useCallback(
    (utcDate: Date | string, format?: string): string => {
      return formatDateInTimezone(utcDate, timezone, format)
    },
    [timezone]
  )

  /**
   * Update user's timezone
   * TODO: Call PUT /api/users/me/timezone when backend is ready
   */
  const updateTimezone = useCallback(async (newTimezone: string): Promise<void> => {
    if (!isValidTimezone(newTimezone)) {
      setError(t('timezone.updateError'))
      throw new Error(t('timezone.updateError'))
    }

    setIsLoading(true)
    setError(null)

    try {
      // TODO: Replace with actual API call
      // const response = await apiClient.put('/api/users/me/timezone', {
      //   timezone: newTimezone,
      // })

      // For now, just update local state
      setTimezone(newTimezone)
      localStorage.setItem('user_timezone', newTimezone)

      // Simulate API delay
      await new Promise((resolve) => setTimeout(resolve, 300))
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : t('timezone.updateError')
      setError(errorMessage)
      throw err
    } finally {
      setIsLoading(false)
    }
  }, [t])

  /**
   * Reset timezone to suggested value
   */
  const resetToSuggested = useCallback(() => {
    const suggested = getSuggestedTimezone()
    setTimezone(suggested)
    localStorage.removeItem('user_timezone')
  }, [])

  /**
   * Get current timezone
   */
  const getCurrentTimezone = useCallback((): string => {
    return timezone
  }, [timezone])

  return {
    currentTimezone: timezone,
    formatDateLocal,
    updateTimezone,
    resetToSuggested,
    getCurrentTimezone,
    isLoading,
    error,
  }
}

/**
 * Hook to format a specific date in user's timezone
 */
export function useFormatDateInTimezone() {
  const { formatDateLocal } = useTimezone()
  return formatDateLocal
}

/**
 * Hook to get current timezone string
 */
export function useCurrentTimezone(): string {
  const { currentTimezone } = useTimezone()
  return currentTimezone
}

/**
 * Hook to update timezone
 */
export function useUpdateTimezone() {
  const { updateTimezone, isLoading, error } = useTimezone()
  return { updateTimezone, isLoading, error }
}
