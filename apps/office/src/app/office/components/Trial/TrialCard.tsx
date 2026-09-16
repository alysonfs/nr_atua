import { useUserTrial } from '../../hooks/useUserTrial'
import { useTimezone } from '../../hooks/useTimezone'
import { useTranslation } from 'react-i18next'

interface TrialCardProps {
  /**
   * Optional: Custom CSS class
   */
  className?: string
  /**
   * Optional: Show CTA button
   * @default false
   */
  showCTA?: boolean
  /**
   * Optional: Callback when CTA is clicked
   */
  onUpgradeClick?: () => void
}

/**
 * RF-003: Trial Card Component
 * 
 * Displays detailed trial information in a card format suitable for:
 * - Dashboard (prominent placement when < 2 days)
 * - Settings/Account preferences
 * 
 * Shows:
 * - Trial status and expiration date
 * - Days/hours remaining
 * - Warning icon when < 2 days
 * - Optional upgrade CTA
 */
export function TrialCard({ className = '', showCTA = false, onUpgradeClick }: TrialCardProps) {
  const { t } = useTranslation()
  const { trial, summary } = useUserTrial()
  const { formatDateLocal } = useTimezone()

  if (!trial || !summary) {
    return null
  }

  // Determine styling based on status
  const isExpired = summary.isExpired
  const isAboutToExpire = summary.isAboutToExpire
  const isUrgent = summary.isUrgent

  let headerColor = 'border-emerald-200 bg-emerald-50'
  let headerTextColor = 'text-emerald-900'
  let icon = '✓'
  let statusText = t('trial.active')

  if (isExpired) {
    headerColor = 'border-red-200 bg-red-50'
    headerTextColor = 'text-red-900'
    icon = '✕'
    statusText = t('trial.expired')
  } else if (isUrgent) {
    headerColor = 'border-red-200 bg-red-50'
    headerTextColor = 'text-red-900'
    icon = '⚠'
    statusText = t('trial.expiresSoon')
  } else if (isAboutToExpire) {
    headerColor = 'border-amber-200 bg-amber-50'
    headerTextColor = 'text-amber-900'
    icon = '⚠'
    statusText = t('trial.expiresSoon')
  }

  const expirationDate = formatDateLocal(trial.expiresAtUtc, 'dd/MM/yyyy')
  const expirationTime = formatDateLocal(trial.expiresAtUtc, 'HH:mm')
  const activationDate = formatDateLocal(trial.activatedAtUtc, 'dd/MM/yyyy')

  return (
    <div
      className={`rounded-lg border-2 bg-white shadow-sm ${headerColor} ${className}`}
    >
      {/* Header */}
      <div className="border-b-2 border-inherit px-6 py-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <span className="text-2xl">{icon}</span>
            <div>
              <h3 className={`text-lg font-semibold ${headerTextColor}`}>
                {statusText}
              </h3>
              <p className={`text-sm opacity-75 ${headerTextColor}`}>
                {t('trial.freePlan')}
              </p>
            </div>
          </div>
        </div>
      </div>

      {/* Content */}
      <div className="space-y-4 px-6 py-4">
        {/* Status Summary */}
        <div className="grid grid-cols-2 gap-4">
          <div className="rounded-lg bg-slate-50 p-3">
            <p className="text-xs font-semibold text-slate-600 uppercase tracking-wider">
              {t('trial.daysRemaining')}
            </p>
            <p className="mt-1 text-2xl font-bold text-slate-900">
              {summary.daysRemaining}
            </p>
          </div>
          <div className="rounded-lg bg-slate-50 p-3">
            <p className="text-xs font-semibold text-slate-600 uppercase tracking-wider">
              {t('trial.hoursRemaining')}
            </p>
            <p className="mt-1 text-2xl font-bold text-slate-900">
              {summary.hoursRemaining}
            </p>
          </div>
        </div>

        {/* Details */}
        <div className="space-y-2 border-t-2 border-slate-200 pt-4">
          <div className="flex justify-between text-sm">
            <span className="text-slate-600">{t('trial.activatedAt')}</span>
            <span className="font-medium text-slate-900">{activationDate}</span>
          </div>
          <div className="flex justify-between text-sm">
            <span className="text-slate-600">{t('trial.expiresAt')}</span>
            <span className="font-medium text-slate-900">
              {expirationDate} ({expirationTime})
            </span>
          </div>
        </div>

        {/* Warning Message */}
        {isAboutToExpire && !isExpired && (
          <div className="rounded-lg border-l-4 border-amber-500 bg-amber-50 p-3">
            <p className="text-sm text-amber-800">
              {isUrgent
                ? t('trial.urgentMessage')
                : t('trial.warningMessage')}
            </p>
          </div>
        )}

        {isExpired && (
          <div className="rounded-lg border-l-4 border-red-500 bg-red-50 p-3">
            <p className="text-sm text-red-800">
              {t('trial.expiredMessage')}
            </p>
          </div>
        )}
      </div>

      {/* Footer with CTA */}
      {showCTA && !isExpired && (
        <div className="border-t-2 border-slate-200 bg-slate-50 px-6 py-4">
          <button
            onClick={onUpgradeClick}
            className="w-full rounded-lg bg-blue-600 px-4 py-2 font-semibold text-white transition-colors hover:bg-blue-700 active:bg-blue-800"
            aria-label={t('trial.upgradeAria')}
          >
            {t('trial.upgrade')}
          </button>
        </div>
      )}

      {showCTA && isExpired && (
        <div className="border-t-2 border-slate-200 bg-slate-50 px-6 py-4">
          <button
            onClick={onUpgradeClick}
            className="w-full rounded-lg bg-blue-600 px-4 py-2 font-semibold text-white transition-colors hover:bg-blue-700 active:bg-blue-800"
            aria-label={t('trial.contactSalesAria')}
          >
            {t('trial.contactSales')}
          </button>
        </div>
      )}
    </div>
  )
}

export default TrialCard
