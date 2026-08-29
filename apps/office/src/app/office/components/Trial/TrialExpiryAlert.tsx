import { useUserTrial } from '../../hooks/useUserTrial'

interface TrialExpiryAlertProps {
  /**
   * Optional: Custom CSS class
   */
  className?: string
  /**
   * Optional: Show close button
   * @default true
   */
  showClose?: boolean
  /**
   * Optional: Callback when close button is clicked
   */
  onClose?: () => void
  /**
   * Optional: Callback when CTA is clicked
   */
  onActionClick?: () => void
}

/**
 * RF-003: Trial Expiry Alert Component
 * 
 * Displays a banner alert when trial is about to expire (< 2 days).
 * 
 * Suitable for:
 * - Top of the page (full width banner)
 * - Above main content
 * 
 * Shows:
 * - Warning message with urgency
 * - Time remaining
 * - CTA for upgrade/extension
 * - Close button to dismiss
 */
export function TrialExpiryAlert({
  className = '',
  showClose = true,
  onClose,
  onActionClick,
}: TrialExpiryAlertProps) {
  const { summary } = useUserTrial()

  // Only show when trial is about to expire
  if (!summary || (!summary.isAboutToExpire && !summary.isUrgent)) {
    return null
  }

  const isUrgent = summary.isUrgent

  return (
    <div
      className={`flex items-center justify-between gap-4 border-l-4 border-red-500 bg-red-50 px-6 py-4 text-red-900 ${className}`}
      role="alert"
    >
      <div className="flex items-center gap-4">
        <span className="text-2xl" aria-hidden="true">
          ⚠
        </span>
        <div>
          {isUrgent ? (
            <>
              <h3 className="font-semibold">
                Seu Trial expira em {summary.hoursRemaining} {summary.hoursRemaining === 1 ? 'hora' : 'horas'}!
              </h3>
              <p className="text-sm opacity-90">
                Faça upgrade agora para manter suas integrações ativas e dados seguros.
              </p>
            </>
          ) : (
            <>
              <h3 className="font-semibold">
                Seu Trial expira em {summary.daysRemaining} {summary.daysRemaining === 1 ? 'dia' : 'dias'}.
              </h3>
              <p className="text-sm opacity-90">
                Configure suas integrações agora ou faça upgrade para continuar usando o ATUA.
              </p>
            </>
          )}
        </div>
      </div>

      <div className="flex shrink-0 items-center gap-2">
        {onActionClick && (
          <button
            onClick={onActionClick}
            className="whitespace-nowrap rounded-lg bg-red-600 px-4 py-2 font-semibold text-white transition-colors hover:bg-red-700 active:bg-red-800"
            aria-label="Fazer upgrade"
          >
            Fazer Upgrade
          </button>
        )}
        {showClose && (
          <button
            onClick={onClose}
            className="rounded-lg p-1 hover:bg-red-100 active:bg-red-200"
            aria-label="Fechar aviso"
          >
            <span aria-hidden="true">✕</span>
          </button>
        )}
      </div>
    </div>
  )
}

export default TrialExpiryAlert
