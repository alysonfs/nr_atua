/**
 * Tempo no pool em formato relativo (ex.: "há 2 h", "há 3 dias"), usado na
 * coluna "tempo no pool" da lista do pool (C1). Sem dependência externa:
 * `Intl.RelativeTimeFormat` já cobre pluralização e locale.
 */
export function formatRelativeTime(isoDate: string, locale: string, nowMs: number = Date.now()): string {
  const then = new Date(isoDate).getTime()
  if (Number.isNaN(then)) return '—'

  const diffMs = then - nowMs
  const diffSeconds = Math.round(diffMs / 1000)
  const formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' })

  const absSeconds = Math.abs(diffSeconds)
  if (absSeconds < 60) return formatter.format(diffSeconds, 'second')

  const diffMinutes = Math.round(diffSeconds / 60)
  if (Math.abs(diffMinutes) < 60) return formatter.format(diffMinutes, 'minute')

  const diffHours = Math.round(diffMinutes / 60)
  if (Math.abs(diffHours) < 24) return formatter.format(diffHours, 'hour')

  const diffDays = Math.round(diffHours / 24)
  return formatter.format(diffDays, 'day')
}
