import { getCoreRowModel, useReactTable, type ColumnDef } from '@tanstack/react-table'
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { DataTable } from '../../../shared/components/ui/Table'
import { formatEstimatedCost } from '../lib/formatEstimatedCost'
import type { AiUsageModel } from '../../../shared/types/aiUsage'

interface AiUsageModelsTableProps {
  models: AiUsageModel[]
  currency: string
}

/**
 * Tabela de consumo/custo por modelo (`GET /api/llm-usage/summary`, campo
 * `models`), ordenada por custo estimado decrescente.
 */
export function AiUsageModelsTable({ models, currency }: AiUsageModelsTableProps) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'pt-BR'
  const numberFormatter = useMemo(() => new Intl.NumberFormat(locale), [locale])

  const sortedModels = useMemo(() => [...models].sort((a, b) => b.estimatedCost - a.estimatedCost), [models])

  const columns = useMemo<ColumnDef<AiUsageModel>[]>(
    () => [
      {
        accessorKey: 'modelId',
        header: t('aiUsage.models.columns.model'),
        cell: (info) => info.getValue<string>() ?? '—',
      },
      {
        accessorKey: 'estimatedCost',
        header: t('aiUsage.models.columns.estimatedCost'),
        cell: (info) => formatEstimatedCost(info.getValue<number>(), currency, locale),
      },
      {
        accessorKey: 'calls',
        header: t('aiUsage.models.columns.calls'),
        cell: (info) => numberFormatter.format(info.getValue<number>()),
      },
      {
        accessorKey: 'inputTokens',
        header: t('aiUsage.models.columns.inputTokens'),
        cell: (info) => numberFormatter.format(info.getValue<number>()),
      },
      {
        accessorKey: 'outputTokens',
        header: t('aiUsage.models.columns.outputTokens'),
        cell: (info) => numberFormatter.format(info.getValue<number>()),
      },
    ],
    [t, currency, locale, numberFormatter],
  )

  const table = useReactTable({ data: sortedModels, columns, getCoreRowModel: getCoreRowModel() })

  return (
    <section aria-labelledby="ai-usage-models-heading" className="space-y-3">
      <h2 id="ai-usage-models-heading" className="text-sm font-semibold text-slate-700">
        {t('aiUsage.models.title')}
      </h2>
      <DataTable table={table} emptyMessage={t('aiUsage.models.empty')} />
    </section>
  )
}
