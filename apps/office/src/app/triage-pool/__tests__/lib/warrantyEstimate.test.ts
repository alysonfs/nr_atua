import { describe, it, expect } from 'vitest'
import {
  formatWarrantyPeriodLabel,
  translateWarrantyEstimateLabel,
  warrantyEstimateTone,
} from '../../lib/warrantyEstimate'
import i18n from '../../../../i18n'

const t = i18n.getFixedT('pt-BR')

describe('warrantyEstimateTone', () => {
  it('maps each status to the expected tone', () => {
    expect(warrantyEstimateTone('ProvavelmenteNaGarantia')).toBe('success')
    expect(warrantyEstimateTone('ProvavelmenteForaDaGarantia')).toBe('danger')
    expect(warrantyEstimateTone('Indeterminado')).toBe('neutral')
    expect(warrantyEstimateTone(undefined)).toBe('neutral')
  })
})

describe('formatWarrantyPeriodLabel', () => {
  it('renders "1 ano" for 12 months', () => {
    expect(formatWarrantyPeriodLabel(t, 12)).toBe('1 ano')
  })

  it('renders "{{count}} anos" for multiples of 12', () => {
    expect(formatWarrantyPeriodLabel(t, 24)).toBe('2 anos')
  })

  it('renders "{{count}} meses" for non-multiples of 12', () => {
    expect(formatWarrantyPeriodLabel(t, 6)).toBe('6 meses')
  })

  it('renders the generic "prazo padrão" text when months is absent', () => {
    expect(formatWarrantyPeriodLabel(t, undefined)).toBe('prazo padrão')
  })
})

describe('translateWarrantyEstimateLabel', () => {
  it('renders the "likely in warranty" text with the estimate note', () => {
    const label = translateWarrantyEstimateLabel(t, { status: 'ProvavelmenteNaGarantia', defaultMonths: 12 })
    expect(label).toBe('Provavelmente na garantia (estimativa: 1 ano)')
  })

  it('renders the "likely out of warranty" text', () => {
    const label = translateWarrantyEstimateLabel(t, { status: 'ProvavelmenteForaDaGarantia', defaultMonths: 12 })
    expect(label).toBe('Provavelmente fora da garantia (estimativa: 1 ano)')
  })

  it('renders "indeterminate" when status or the estimate itself is absent', () => {
    expect(translateWarrantyEstimateLabel(t, { status: 'Indeterminado' })).toBe('Garantia indeterminada')
    expect(translateWarrantyEstimateLabel(t, undefined)).toBe('Garantia indeterminada')
  })
})
