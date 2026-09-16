import type { TimezoneOption } from '../../../shared/types/timezone'
import { POPULAR_TIMEZONES, TIMEZONE_SUGGESTIONS_BY_LANGUAGE } from '../../../shared/types/timezone'

/**
 * Get all available IANA timezones
 * This is a curated list of common timezones
 * For a complete list, consider using a library like date-fns-tz or moment-timezone
 */
const IANA_TIMEZONES = [
  // Africa
  'Africa/Cairo',
  'Africa/Casablanca',
  'Africa/Johannesburg',
  'Africa/Lagos',
  'Africa/Nairobi',
  'Africa/Tunis',

  // Americas
  'America/Anchorage',
  'America/Argentina/Buenos_Aires',
  'America/Chicago',
  'America/Denver',
  'America/Los_Angeles',
  'America/Mexico_City',
  'America/New_York',
  'America/Sao_Paulo',
  'America/Toronto',
  'America/Vancouver',

  // Asia
  'Asia/Bangkok',
  'Asia/Dubai',
  'Asia/Hong_Kong',
  'Asia/Kolkata',
  'Asia/Jakarta',
  'Asia/Manila',
  'Asia/Seoul',
  'Asia/Shanghai',
  'Asia/Singapore',
  'Asia/Tokyo',

  // Australia
  'Australia/Adelaide',
  'Australia/Brisbane',
  'Australia/Melbourne',
  'Australia/Sydney',

  // Europe
  'Europe/Amsterdam',
  'Europe/Athens',
  'Europe/Berlin',
  'Europe/Brussels',
  'Europe/Budapest',
  'Europe/Dublin',
  'Europe/Helsinki',
  'Europe/Lisbon',
  'Europe/London',
  'Europe/Madrid',
  'Europe/Moscow',
  'Europe/Paris',
  'Europe/Prague',
  'Europe/Rome',
  'Europe/Stockholm',
  'Europe/Zurich',

  // Pacific
  'Pacific/Auckland',
  'Pacific/Fiji',
  'Pacific/Honolulu',

  // UTC
  'UTC',
]

/**
 * Format UTC offset for display
 * @param offset - UTC offset in milliseconds
 * @returns Formatted offset string like "UTC-3" or "UTC+5:30"
 */
export function formatUTCOffset(offset: number): string {
  const sign = offset >= 0 ? '+' : '-'
  const absoluteOffset = Math.abs(offset)
  const hours = Math.floor(absoluteOffset / 3600000)
  const minutes = Math.floor((absoluteOffset % 3600000) / 60000)

  if (minutes === 0) {
    return `UTC${sign}${hours}`
  }
  return `UTC${sign}${hours}:${minutes.toString().padStart(2, '0')}`
}

/**
 * Get current UTC offset for a timezone
 * @param timezone - IANA timezone string
 * @returns UTC offset in milliseconds
 */
export function getUTCOffset(timezone: string): number {
  try {
    const date = new Date()
    const utcDate = new Date(date.toLocaleString('en-US', { timeZone: 'UTC' }))
    const tzDate = new Date(date.toLocaleString('en-US', { timeZone: timezone }))
    return tzDate.getTime() - utcDate.getTime()
  } catch {
    // If timezone is invalid, return 0 (UTC)
    return 0
  }
}

/**
 * Format a UTC date to local timezone string
 * @param utcDate - Date object or ISO string in UTC
 * @param timezone - IANA timezone string
 * @param format - Output format (default: "dd/MM/yyyy HH:mm")
 * @returns Formatted date string
 */
export function formatDateInTimezone(
  utcDate: Date | string,
  timezone: string,
  format: string = 'dd/MM/yyyy HH:mm'
): string {
  try {
    const date = typeof utcDate === 'string' ? new Date(utcDate) : utcDate

    // Use Intl.DateTimeFormat for timezone-aware formatting
    const formatter = new Intl.DateTimeFormat('pt-BR', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      timeZone: timezone,
    })

    const parts = formatter.formatToParts(date)
    const values: Record<string, string> = {}

    parts.forEach((part) => {
      if (part.type !== 'literal') {
        values[part.type] = part.value
      }
    })

    if (format === 'dd/MM/yyyy HH:mm') {
      return `${values.day}/${values.month}/${values.year} ${values.hour}:${values.minute}`
    }
    if (format === 'dd/MM/yyyy') {
      return `${values.day}/${values.month}/${values.year}`
    }
    if (format === 'HH:mm') {
      return `${values.hour}:${values.minute}`
    }

    // Default format
    return `${values.day}/${values.month}/${values.year} ${values.hour}:${values.minute}`
  } catch (error) {
    // Fallback to UTC if timezone is invalid
    console.warn(`Invalid timezone: ${timezone}`, error)
    const date = typeof utcDate === 'string' ? new Date(utcDate) : utcDate
    return date.toISOString().split('T')[0]
  }
}

/**
 * Get all available timezone options
 * @returns Array of TimezoneOption objects
 */
export function getAllTimezoneOptions(): TimezoneOption[] {
  return IANA_TIMEZONES.map((timezone) => ({
    timezone,
    label: `${timezone} (${formatUTCOffset(getUTCOffset(timezone))})`,
    offset: formatUTCOffset(getUTCOffset(timezone)),
  }))
}

/**
 * Search timezone options by query
 * @param query - Search string
 * @returns Array of matching TimezoneOption objects
 */
export function searchTimezones(query: string): TimezoneOption[] {
  const lowerQuery = query.toLowerCase()
  return getAllTimezoneOptions().filter(
    (option) =>
      option.timezone.toLowerCase().includes(lowerQuery) ||
      formatTimezoneDisplayName(option.timezone).toLowerCase().includes(lowerQuery) ||
      option.label.toLowerCase().includes(lowerQuery) ||
      option.offset.toLowerCase().includes(lowerQuery)
  )
}

/**
 * Get suggested timezone by language
 * @param language - Language code (e.g., "pt-BR", "en-US")
 * @returns IANA timezone string
 */
export function getSuggestedTimezone(language: string = navigator.language): string {
  return TIMEZONE_SUGGESTIONS_BY_LANGUAGE[language] || 'UTC'
}

/**
 * Get popular timezones as options
 * @returns Array of TimezoneOption objects for popular timezones
 */
export function getPopularTimezoneOptions(): TimezoneOption[] {
  return POPULAR_TIMEZONES.map((timezone) => ({
    timezone,
    label: `${timezone} (${formatUTCOffset(getUTCOffset(timezone))})`,
    offset: formatUTCOffset(getUTCOffset(timezone)),
  }))
}

/**
 * Validate if a timezone string is valid IANA format
 * @param timezone - Timezone string to validate
 * @returns true if valid, false otherwise
 */
export function isValidTimezone(timezone: string): boolean {
  try {
    Intl.DateTimeFormat(undefined, { timeZone: timezone })
    return true
  } catch {
    return false
  }
}

/**
 * Format display name for timezone
 * @param timezone - IANA timezone string
 * @returns Human-readable timezone name
 */
export function formatTimezoneDisplayName(timezone: string): string {
  const parts = timezone.split('/')
  if (parts.length === 1) return timezone
  if (parts.length === 2) {
    return parts[1].replace(/_/g, ' ')
  }
  // For timezones like America/Argentina/Buenos_Aires
  return parts.slice(1).join(' - ').replace(/_/g, ' ')
}
