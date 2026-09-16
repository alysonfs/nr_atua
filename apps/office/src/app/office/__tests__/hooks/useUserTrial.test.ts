import { describe, expect, it } from 'vitest'
import { getTrialSummary } from '../../hooks/useUserTrial'
import type { TrialDTO } from '../../../../../shared/types/trial'

const trial: TrialDTO = {
  trialId: 'trial-test',
  activatedAtUtc: '2026-08-22T00:00:00.000Z',
  expiresAtUtc: '2026-08-29T00:00:00.000Z',
  status: 'active',
  daysRemaining: 0,
}

describe('getTrialSummary', () => {
  it('treats the trial as expired at its exact UTC expiration instant', () => {
    const summary = getTrialSummary(trial, new Date('2026-08-29T00:00:00.000Z'))

    expect(summary).toMatchObject({
      isActive: false,
      isExpired: true,
      isAboutToExpire: false,
      isUrgent: false,
    })
  })

  it('treats the trial as active immediately before its UTC expiration instant', () => {
    const summary = getTrialSummary(trial, new Date('2026-08-28T23:59:59.999Z'))

    expect(summary).toMatchObject({
      isActive: true,
      isExpired: false,
    })
  })
})
