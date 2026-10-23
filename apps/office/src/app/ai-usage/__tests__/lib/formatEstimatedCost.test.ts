import { describe, expect, it } from 'vitest'
import { formatEstimatedCost } from '../../lib/formatEstimatedCost'

describe('formatEstimatedCost', () => {
  it('uses 4 fraction digits when the value is below 1 (fractions of a cent)', () => {
    expect(formatEstimatedCost(0.1234, 'USD', 'en-US')).toBe('$0.1234')
  })

  it('uses 2 fraction digits for values of 1 or more', () => {
    expect(formatEstimatedCost(12.3, 'USD', 'en-US')).toBe('$12.30')
  })

  it('uses 2 fraction digits for zero', () => {
    expect(formatEstimatedCost(0, 'USD', 'en-US')).toBe('$0.00')
  })

  it('formats with the pt-BR locale', () => {
    expect(formatEstimatedCost(0.005, 'USD', 'pt-BR')).toContain('0,0050')
  })
})
