import { useCallback, useState } from 'react'
import { useTimezone } from '../../hooks/useTimezone'
import { formatUTCOffset, getUTCOffset, formatTimezoneDisplayName, getSuggestedTimezone } from '../../lib/timezone'
import TimezonePicker from './TimezonePicker'

interface TimezonePreferenceProps {
  /**
   * Optional: Custom CSS class
   */
  className?: string
  /**
   * Optional: Show in a card container
   * @default true
   */
  showCard?: boolean
  /**
   * Optional: Callback when timezone is updated
   */
  onTimezoneUpdated?: (newTimezone: string) => void
}

/**
 * RF-004: Timezone Preference Component
 * 
 * Displays current timezone preference and allows user to change it.
 * Used in:
 * - Account/Preferences page
 * - Settings panel
 * 
 * Shows:
 * - Current timezone with UTC offset
 * - Button to open picker modal
 * - Loading state during update
 * - Error message if update fails
 */
export function TimezonePreference({
  className = '',
  showCard = true,
  onTimezoneUpdated,
}: TimezonePreferenceProps) {
  const { currentTimezone, updateTimezone, isLoading, error } = useTimezone()
  const [isModalOpen, setIsModalOpen] = useState(false)
  const [updateError, setUpdateError] = useState<string | null>(null)

  // Handle timezone update
  const handleTimezoneUpdate = useCallback(
    async (newTimezone: string) => {
      setUpdateError(null)
      try {
        await updateTimezone(newTimezone)
        setIsModalOpen(false)
        onTimezoneUpdated?.(newTimezone)
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : 'Falha ao atualizar fuso horário'
        setUpdateError(errorMessage)
      }
    },
    [updateTimezone, onTimezoneUpdated]
  )

  const offset = formatUTCOffset(getUTCOffset(currentTimezone))
  const displayName = formatTimezoneDisplayName(currentTimezone)
  const suggestedTz = getSuggestedTimezone()

  const content = (
    <div className={`space-y-3 ${className}`}>
      <div>
        <h3 className="text-sm font-semibold text-slate-900">Fuso Horário</h3>
        <p className="text-xs text-slate-600">
          Todas as datas são armazenadas em UTC e exibidas no seu fuso horário selecionado.
        </p>
      </div>

      <div className="flex items-center justify-between rounded-lg bg-slate-50 px-4 py-3">
        <div>
          <div className="font-medium text-slate-900">{displayName}</div>
          <div className="text-sm text-slate-600">{currentTimezone}</div>
          <div className="mt-1 text-xs font-semibold text-slate-600 uppercase tracking-wider">
            {offset}
          </div>
        </div>
        <button
          onClick={() => setIsModalOpen(true)}
          disabled={isLoading}
          className="rounded-lg bg-blue-600 px-4 py-2 font-medium text-white transition-colors hover:bg-blue-700 disabled:bg-slate-400 disabled:cursor-not-allowed active:bg-blue-800"
          aria-label="Alterar fuso horário"
        >
          {isLoading ? 'Salvando...' : 'Alterar'}
        </button>
      </div>

      {/* Error message */}
      {(error || updateError) && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3">
          <p className="text-sm text-red-800">
            {updateError || error || 'Falha ao atualizar fuso horário'}
          </p>
        </div>
      )}

      {/* Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
          <div className="mx-4 max-h-[80vh] w-full max-w-md overflow-y-auto rounded-lg bg-white shadow-lg">
            <TimezonePicker
              currentTimezone={currentTimezone}
              suggestedTimezone={suggestedTz}
              onSelect={handleTimezoneUpdate}
              isModal={true}
              onClose={() => setIsModalOpen(false)}
            />
          </div>
        </div>
      )}
    </div>
  )

  if (!showCard) {
    return content
  }

  return (
    <div className="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      {content}
    </div>
  )
}

export default TimezonePreference
