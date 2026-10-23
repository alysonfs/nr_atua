import { describe, expect, it } from 'vitest'
import { resolveAiUsageRange } from '../../lib/aiUsagePeriod'

describe('resolveAiUsageRange', () => {
  const now = new Date(2026, 9, 23) // 2026-10-23 (local)

  it('resolves the last 7 days (inclusive of today)', () => {
    expect(resolveAiUsageRange('7d', now)).toEqual({ from: '2026-10-17', to: '2026-10-23' })
  })

  it('resolves the last 30 days (inclusive of today)', () => {
    expect(resolveAiUsageRange('30d', now)).toEqual({ from: '2026-09-24', to: '2026-10-23' })
  })

  it('resolves the last 90 days (inclusive of today)', () => {
    expect(resolveAiUsageRange('90d', now)).toEqual({ from: '2026-07-26', to: '2026-10-23' })
  })

  it('resolves the current month, from day 1 to today', () => {
    expect(resolveAiUsageRange('currentMonth', now)).toEqual({ from: '2026-10-01', to: '2026-10-23' })
  })
})
