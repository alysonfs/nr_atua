import { getCoreRowModel, useReactTable, type ColumnDef } from '@tanstack/react-table'
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { DataTable } from '../../../shared/components/ui/Table'
import { formatEstimatedCost } from '../lib/formatEstimatedCost'
import type { AiUsageTenant } from '../../../shared/types/aiUsage'

interface AiUsageTenantsTableProps {
  tenants: AiUsageTenant[]
  currency: string
}

/**
 * Tabela de consumo/custo por tenant (`GET /api/llm-usage/summary`, campo
 * `tenants`), ordenada por custo estimado decrescente — o tenant mais caro
 * no período aparece primeiro.
 */
export function AiUsageTenantsTable({ tenants, currency }: AiUsageTenantsTableProps) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'pt-BR'
  const numberFormatter = useMemo(() => new Intl.NumberFormat(locale), [locale])

  const sortedTenants = useMemo(
    () => [...tenants].sort((a, b) => b.estimatedCost - a.estimatedCost),
    [tenants],
  )

  const columns = useMemo<ColumnDef<AiUsageTenant>[]>(
    () => [
      {
        accessorKey: 'tenantName',
        header: t('aiUsage.tenants.columns.tenant'),
        cell: (info) => info.getValue<string>() ?? '—',
      },
      {
        accessorKey: 'estimatedCost',
        header: t('aiUsage.tenants.columns.estimatedCost'),
        cell: (info) => formatEstimatedCost(info.getValue<number>(), currency, locale),
      },
      {
        accessorKey: 'calls',
        header: t('aiUsage.tenants.columns.calls'),
        cell: (info) => numberFormatter.format(info.getValue<number>()),
      },
      {
        accessorKey: 'inputTokens',
        header: t('aiUsage.tenants.columns.inputTokens'),
        cell: (info) => numberFormatter.format(info.getValue<number>()),
      },
      {
        accessorKey: 'outputTokens',
        header: t('aiUsage.tenants.columns.outputTokens'),
        cell: (info) => numberFormatter.format(info.getValue<number>()),
      },
      {
        accessorKey: 'avgLatencyMs',
        header: t('aiUsage.tenants.columns.avgLatencyMs'),
        cell: (info) => numberFormatter.format(Math.round(info.getValue<number>())),
      },
      {
        accessorKey: 'failedCalls',
        header: t('aiUsage.tenants.columns.failedCalls'),
        cell: (info) => numberFormatter.format(info.getValue<number>()),
      },
    ],
    [t, currency, locale, numberFormatter],
  )

  const table = useReactTable({ data: sortedTenants, columns, getCoreRowModel: getCoreRowModel() })

  return (
    <section aria-labelledby="ai-usage-tenants-heading" className="space-y-3">
      <h2 id="ai-usage-tenants-heading" className="text-sm font-semibold text-slate-700">
        {t('aiUsage.tenants.title')}
      </h2>
      <DataTable table={table} emptyMessage={t('aiUsage.tenants.empty')} />
    </section>
  )
}
