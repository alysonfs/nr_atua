import { useUserTrial } from '../../hooks/useUserTrial'
import { useTimezone } from '../../hooks/useTimezone'
import { useTranslation } from 'react-i18next'

interface TrialBadgeProps {
  /**
   * Optional: Show compact version (for header)
   * @default false
   */
  compact?: boolean
  /**
   * Optional: Custom CSS class
   */
  className?: string
}

/**
 * RF-003: Trial Badge Component
 * 
 * Displays the trial status in a compact format suitable for:
 * - Header (always visible)
 * - Sidebar
 * - Dashboard
 * 
 * Shows:
 * - "Trial ativo até 05/09/2026 (7 dias)" when active
 * - "Trial expirou" when expired
 * - Color changes to red when < 2 days remaining
 */
export function TrialBadge({ compact = false, className = '' }: TrialBadgeProps) {
  const { t } = useTranslation()
  const { trial, summary } = useUserTrial()
  const { formatDateLocal } = useTimezone()

  if (!trial || !summary) {
    return null
  }

  // Determine colors based on status
  let badgeColor = 'bg-emerald-100 text-emerald-800'
  let textColor = 'text-emerald-700'
  let icon = '⏳'

  if (summary.isExpired) {
    badgeColor = 'bg-red-100 text-red-800'
    textColor = 'text-red-700'
    icon = '✕'
  } else if (summary.isAboutToExpire) {
    badgeColor = 'bg-amber-100 text-amber-800'
    textColor = 'text-amber-700'
    icon = '⚠'
  }

  // Format expiration date
  const expirationDate = formatDateLocal(trial.expiresAtUtc, 'dd/MM/yyyy')

  // Determine display text
  const displayText = summary.isExpired
    ? t('trial.badgeExpired')
    : summary.daysRemaining === 0
      ? t('trial.badgeToday', { hours: summary.hoursRemaining })
      : summary.daysRemaining === 1
        ? t('trial.badgeTomorrow')
        : t('trial.badgeUntil', { date: expirationDate, days: summary.daysRemaining })

  if (compact) {
    return (
      <div
        className={`inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-semibold ${badgeColor} ${className}`}
        role="status"
        aria-label={displayText}
      >
        <span className={textColor}>{icon}</span>
        <span>{displayText}</span>
      </div>
    )
  }

  return (
    <div
      className={`flex items-center gap-2 rounded-lg ${badgeColor} px-3 py-2 ${className}`}
      role="status"
      aria-label={displayText}
    >
      <span className={`text-lg ${textColor}`}>{icon}</span>
      <div>
        <div className="text-sm font-semibold">Trial</div>
        <div className="text-xs opacity-75">{displayText}</div>
      </div>
    </div>
  )
}

export default TrialBadge
