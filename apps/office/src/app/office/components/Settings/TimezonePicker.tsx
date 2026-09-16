import { useMemo, useState } from 'react'
import {
  formatTimezoneDisplayName,
  getPopularTimezoneOptions,
  getUTCOffset,
  formatUTCOffset,
  searchTimezones,
} from '../../lib/timezone'
import type { TimezoneOption } from '../../../../shared/types/timezone'
import { useTranslation } from 'react-i18next'

interface TimezonePicerProps {
  /**
   * Currently selected timezone (IANA format)
   */
  currentTimezone: string
  /**
   * Suggested timezone (IANA format)
   */
  suggestedTimezone?: string
  /**
   * Callback when timezone is selected
   */
  onSelect: (timezone: string) => void
  /**
   * Optional: Custom CSS class
   */
  className?: string
  /**
   * Optional: Show in modal mode (with overlay)
   * @default false
   */
  isModal?: boolean
  /**
   * Optional: Callback to close modal
   */
  onClose?: () => void
}

/**
 * RF-004: Timezone Picker Component
 * 
 * Displays a searchable list of timezones with:
 * - Search/filter functionality
 * - Suggested timezone (by language)
 * - Popular timezones
 * - UTC offset display
 * - Current timezone indicator
 * 
 * Can be used as:
 * - Dropdown in preferences
 * - Modal dialog
 */
export function TimezonePicker({
  currentTimezone,
  suggestedTimezone,
  onSelect,
  className = '',
  isModal = false,
  onClose,
}: TimezonePicerProps) {
  const { t } = useTranslation()
  const [searchQuery, setSearchQuery] = useState('')
  const [selectedTz, setSelectedTz] = useState(currentTimezone)

  // Get all timezone options
  const allTimezones = useMemo(() => searchTimezones(searchQuery), [searchQuery])

  // Get popular timezones
  const popularTimezones = useMemo(() => getPopularTimezoneOptions(), [])

  // Determine which timezones to display
  const displayedTimezones = useMemo(() => {
    if (searchQuery.trim()) {
      return allTimezones
    }
    return popularTimezones
  }, [searchQuery, allTimezones, popularTimezones])

  // Handle timezone selection
  const handleSelect = (timezone: string) => {
    setSelectedTz(timezone)
    onSelect(timezone)
    if (isModal && onClose) {
      // Allow time for selection to complete
      setTimeout(onClose, 100)
    }
  }

  return (
    <div
      className={`flex flex-col rounded-lg border border-slate-200 bg-white ${className}`}
    >
      {/* Header */}
      <div className="border-b border-slate-200 p-4">
        <h2 className="mb-3 font-semibold text-slate-900">
          {t('timezone.pickerTitle')}
        </h2>
        <div className="relative">
          <input
            type="text"
            placeholder={t('timezone.search')}
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
            aria-label={t('timezone.search')}
          />
          {searchQuery && (
            <button
              onClick={() => setSearchQuery('')}
              className="absolute right-3 top-2 text-slate-400 hover:text-slate-600"
              aria-label={t('timezone.clearSearch')}
            >
              ✕
            </button>
          )}
        </div>
      </div>

      {/* Suggested timezone section (when no search query) */}
      {!searchQuery && suggestedTimezone && suggestedTimezone !== currentTimezone && (
        <div className="border-b border-slate-200">
          <div className="px-4 py-2">
            <p className="text-xs font-semibold text-slate-600 uppercase tracking-wider">
              {t('timezone.suggested')}
            </p>
          </div>
          <TimezoneSuggestedItem
            timezone={suggestedTimezone}
            isSelected={selectedTz === suggestedTimezone}
            isCurrent={suggestedTimezone === currentTimezone}
            onSelect={handleSelect}
          />
        </div>
      )}

      {/* Current timezone section */}
      {!searchQuery && (
        <div className="border-b border-slate-200">
          <div className="px-4 py-2">
            <p className="text-xs font-semibold text-slate-600 uppercase tracking-wider">
              {t('timezone.currentSection')}
            </p>
          </div>
          <TimezoneSuggestedItem
            timezone={currentTimezone}
            isSelected={selectedTz === currentTimezone}
            isCurrent={true}
            onSelect={handleSelect}
          />
        </div>
      )}

      {/* Timezones list */}
      <div className="max-h-96 overflow-y-auto">
        {displayedTimezones.length === 0 ? (
          <div className="px-4 py-8 text-center text-sm text-slate-500">
            {t('timezone.empty')}
          </div>
        ) : (
          <div className="divide-y divide-slate-100">
            {displayedTimezones.map((option) => (
              <TimezoneItem
                key={option.timezone}
                option={option}
                isSelected={selectedTz === option.timezone}
                isCurrent={option.timezone === currentTimezone}
                onSelect={handleSelect}
              />
            ))}
          </div>
        )}
      </div>

      {/* Footer (Modal mode) */}
      {isModal && (
        <div className="flex justify-end gap-2 border-t border-slate-200 bg-slate-50 px-4 py-3">
          <button
            onClick={onClose}
            className="rounded-lg px-4 py-2 font-medium text-slate-700 hover:bg-slate-100 active:bg-slate-200"
          >
            {t('timezone.cancel')}
          </button>
          <button
            onClick={() => {
              onSelect(selectedTz)
              if (onClose) onClose()
            }}
            className="rounded-lg bg-blue-600 px-4 py-2 font-medium text-white hover:bg-blue-700 active:bg-blue-800"
          >
            {t('timezone.save')}
          </button>
        </div>
      )}
    </div>
  )
}

/**
 * Individual timezone option item
 */
interface TimezoneItemProps {
  option: TimezoneOption
  isSelected: boolean
  isCurrent: boolean
  onSelect: (timezone: string) => void
}

function TimezoneItem({
  option,
  isSelected,
  isCurrent,
  onSelect,
}: TimezoneItemProps) {
  const { t } = useTranslation()
  return (
    <button
      onClick={() => onSelect(option.timezone)}
      className={`w-full px-4 py-3 text-left transition-colors hover:bg-blue-50 ${
        isSelected ? 'bg-blue-100' : ''
      }`}
      aria-pressed={isSelected}
    >
      <div className="flex items-center justify-between">
        <div className="flex-1">
          <div className="font-medium text-slate-900">
            {formatTimezoneDisplayName(option.timezone)}
          </div>
          <div className="text-xs text-slate-500">{option.timezone}</div>
        </div>
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-slate-600">{option.offset}</span>
          {isCurrent && (
            <span className="rounded-full bg-emerald-100 px-2 py-1 text-xs font-semibold text-emerald-700">
              {t('timezone.current')}
            </span>
          )}
          {isSelected && !isCurrent && (
            <span className="rounded-full bg-blue-100 px-2 py-1 text-xs font-semibold text-blue-700">
              ✓
            </span>
          )}
        </div>
      </div>
    </button>
  )
}

/**
 * Suggested/Current timezone item (with emphasis)
 */
function TimezoneSuggestedItem({
  timezone,
  isSelected,
  isCurrent,
  onSelect,
}: Omit<TimezoneItemProps, 'option'> & { timezone: string }) {
  const { t } = useTranslation()
  const offset = formatUTCOffset(getUTCOffset(timezone))

  return (
    <button
      onClick={() => onSelect(timezone)}
      className={`w-full px-4 py-3 text-left transition-colors hover:bg-blue-50 ${
        isSelected ? 'bg-blue-100' : ''
      }`}
      aria-pressed={isSelected}
    >
      <div className="flex items-center justify-between">
        <div className="flex-1">
          <div className="font-semibold text-slate-900">
            {formatTimezoneDisplayName(timezone)}
          </div>
          <div className="text-xs text-slate-500">{timezone}</div>
        </div>
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-slate-600">{offset}</span>
          {isCurrent && (
            <span className="rounded-full bg-emerald-100 px-2 py-1 text-xs font-semibold text-emerald-700">
              {t('timezone.current')}
            </span>
          )}
        </div>
      </div>
    </button>
  )
}

export default TimezonePicker
