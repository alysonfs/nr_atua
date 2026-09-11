/**
 * Test suite for useServiceOrderMonthSummary (mock data hook).
 *
 * Garante que a geração de dados é determinística (mesma entrada produz
 * a mesma saída, sem uso de Math.random), o que é essencial para os
 * testes de snapshot/comportamento dos componentes que consomem o hook.
 */

import { describe, it, expect } from 'vitest'
import { useServiceOrderMonthSummary } from '../../hooks/useServiceOrderMonthSummary'

describe('useServiceOrderMonthSummary', () => {
  const referenceDate = new Date(2026, 8, 10) // 10/09/2026

  it('is deterministic for the same reference date', () => {
    const first = useServiceOrderMonthSummary(referenceDate)
    const second = useServiceOrderMonthSummary(referenceDate)

    expect(first).toEqual(second)
  })

  it('returns one entry per tracked status', () => {
    const summary = useServiceOrderMonthSummary(referenceDate)

    expect(summary.map((entry) => entry.status)).toEqual([
      'Designado',
      'Em Processamento',
      'Concluído',
      'Cancelado',
    ])
  })

  it('returns a daily count entry for each day of the reference month', () => {
    const summary = useServiceOrderMonthSummary(referenceDate)
    const daysInSeptember2026 = 30

    summary.forEach((entry) => {
      expect(entry.dailyCounts).toHaveLength(daysInSeptember2026)
      expect(entry.total).toBe(entry.dailyCounts.reduce((sum, value) => sum + value, 0))
    })
  })

  it('never returns negative counts', () => {
    const summary = useServiceOrderMonthSummary(referenceDate)

    summary.forEach((entry) => {
      entry.dailyCounts.forEach((count) => {
        expect(count).toBeGreaterThanOrEqual(0)
      })
    })
  })
})
