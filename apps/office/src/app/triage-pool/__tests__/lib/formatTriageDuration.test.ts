/**
 * Test suite for formatShortDuration / formatTriageDurationLabel.
 *
 * Cobre:
 * - Formato curto da duração (minutos, horas+minutos, dias+horas).
 * - "—" sem `startedAtUtc`.
 * - "em andamento" quando `finishedAtUtc` é `null`/ausente.
 * - "finalizada em <duração>" quando `finishedAtUtc` existe.
 */

import { describe, expect, it } from 'vitest'
import i18n, { DEFAULT_LOCALE } from '../../../../i18n'
import { formatShortDuration, formatTriageDurationLabel } from '../../lib/formatTriageDuration'

const t = i18n.getFixedT(DEFAULT_LOCALE)

describe('formatShortDuration', () => {
  it('formats durations under a minute', () => {
    expect(formatShortDuration(30_000)).toBe('< 1 min')
  })

  it('formats durations in minutes', () => {
    expect(formatShortDuration(12 * 60_000)).toBe('12 min')
  })

  it('formats durations in hours and minutes', () => {
    expect(formatShortDuration(65 * 60_000)).toBe('1 h 05 min')
  })

  it('formats durations in days and hours', () => {
    expect(formatShortDuration((2 * 24 + 3) * 60 * 60_000)).toBe('2 d 3 h')
  })
})

describe('formatTriageDurationLabel', () => {
  it('returns "—" when startedAtUtc is missing', () => {
    expect(formatTriageDurationLabel(t, undefined, null, Date.now())).toBe('—')
    expect(formatTriageDurationLabel(t, null, null, Date.now())).toBe('—')
  })

  it('shows "in progress" when finishedAtUtc is null', () => {
    const now = new Date('2026-10-20T11:00:00Z').getTime()
    const label = formatTriageDurationLabel(t, '2026-10-20T10:00:00Z', null, now)
    expect(label).toBe('1 h 00 min (em andamento)')
  })

  it('shows "finished in <duration>" when finishedAtUtc is set', () => {
    const now = new Date('2026-10-20T12:00:00Z').getTime()
    const label = formatTriageDurationLabel(t, '2026-10-20T10:00:00Z', '2026-10-20T10:12:00Z', now)
    expect(label).toBe('Finalizada em 12 min')
  })
})
