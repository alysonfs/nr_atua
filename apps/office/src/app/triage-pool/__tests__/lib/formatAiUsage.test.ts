import { describe, it, expect } from 'vitest'
import { formatAiUsageCost, formatAiUsageListLabel, formatAiUsageDetailLabel } from '../../lib/formatAiUsage'
import i18n from '../../../../i18n'

const t = i18n.getFixedT('pt-BR')

describe('formatAiUsageCost', () => {
  it('uses 4 decimal places for values below 1 USD', () => {
    expect(formatAiUsageCost(0.0123, 'pt-BR')).toMatch(/0,0123/)
  })

  it('uses 2 decimal places for values of 1 USD or more', () => {
    expect(formatAiUsageCost(1.5, 'pt-BR')).toMatch(/1,50/)
  })
})

describe('formatAiUsageListLabel', () => {
  it('returns "—" when llmUsage or estimatedCostUsd is absent', () => {
    expect(formatAiUsageListLabel(undefined, 'pt-BR')).toBe('—')
    expect(formatAiUsageListLabel({}, 'pt-BR')).toBe('—')
  })

  it('returns the formatted cost when estimatedCostUsd is present', () => {
    expect(formatAiUsageListLabel({ estimatedCostUsd: 0.05 }, 'pt-BR')).toMatch(/0,05/)
  })
})

describe('formatAiUsageDetailLabel', () => {
  it('returns the "unavailable" text when llmUsage is absent', () => {
    expect(formatAiUsageDetailLabel(t, undefined, 'pt-BR')).toBe('—')
  })

  it('combines cost, tokens and calls when all are present', () => {
    const label = formatAiUsageDetailLabel(
      t,
      { estimatedCostUsd: 0.0123, inputTokens: 1200, outputTokens: 340, calls: 3 },
      'pt-BR',
    )
    expect(label).toMatch(/0,0123/)
    expect(label).toMatch(/1\.200 tokens de entrada \/ 340 de saída/)
    expect(label).toMatch(/3 chamadas/)
  })

  it('shows only the cost when tokens and calls are absent', () => {
    const label = formatAiUsageDetailLabel(t, { estimatedCostUsd: 0.5 }, 'pt-BR')
    expect(label).toMatch(/0,50/)
    expect(label).not.toMatch(/tokens/)
    expect(label).not.toMatch(/chamadas/)
  })
})
