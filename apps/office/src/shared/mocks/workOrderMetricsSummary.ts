/**
 * Mock data for the work order metrics summary — used during development
 * until the backend endpoint below is available:
 *
 *   GET /api/tenants/{tenantId}/work-orders/metrics-summary?months=12
 *
 * Contract (shape must stay in sync with the real endpoint):
 * {
 *   "trend": [{ "month": "2025-10", "created": 34, "completed": 28 }],
 *   "statusDistribution": [{ "status": "closed", "total": 120 }]
 * }
 */
import type { WorkOrderMetricsSummary } from '../../app/dashboard/hooks/useWorkOrderMetricsSummary'

function buildMockTrend(months: number): WorkOrderMetricsSummary['trend'] {
  const now = new Date()
  return Array.from({ length: months }, (_, index) => {
    const date = new Date(now.getFullYear(), now.getMonth() - (months - 1 - index), 1)
    const month = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`
    const created = 20 + Math.round(Math.sin(index / 2) * 8) + index
    const completed = Math.max(0, created - 4 - Math.round(Math.cos(index / 3) * 3))
    return { month, created, completed }
  })
}

export const mockWorkOrderMetricsSummary: WorkOrderMetricsSummary = {
  trend: buildMockTrend(12),
  statusDistribution: [
    { status: 'closed', total: 120 },
    { status: 'assigned', total: 34 },
    { status: 'pending', total: 18 },
    { status: 'cancelled', total: 9 },
    { status: 'payment approved', total: 27 },
  ],
}
