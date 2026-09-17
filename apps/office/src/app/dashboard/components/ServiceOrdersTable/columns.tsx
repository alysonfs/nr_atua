import type { ColumnDef } from '@tanstack/react-table'
import type { TFunction } from 'i18next'
import type { WorkOrderListItem } from '../../hooks/useWorkOrdersByStatus'

/** Formata uma data ISO 8601 no padrão do locale ativo (dd/mm/aaaa hh:mm). */
function formatDateTime(isoDate: string | null, locale: string): string {
  if (!isoDate) return '—'
  return new Date(isoDate).toLocaleString(locale, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

/**
 * Definição das colunas da tabela de ordens de serviço, usada pelo
 * `@tanstack/react-table` via `useReactTable`.
 */
export function createServiceOrdersColumns(
  t: TFunction,
  locale: string,
): ColumnDef<WorkOrderListItem>[] {
  return [
    {
      accessorKey: 'workOrderProviderNo',
      header: t('common.providerOrderNumber'),
      cell: (info) => info.getValue<string | null>() ?? '—',
    },
    {
      accessorKey: 'status',
      header: t('common.status'),
    },
    {
      accessorKey: 'providerCreatedAt',
      header: t('common.providerCreatedAt'),
      cell: (info) => formatDateTime(info.getValue<string | null>(), locale),
    },
    {
      accessorKey: 'providerUpdatedAt',
      header: t('common.providerUpdatedAt'),
      cell: (info) => formatDateTime(info.getValue<string | null>(), locale),
    },
    {
      accessorKey: 'productModel',
      header: t('common.equipmentModel'),
      cell: (info) => info.getValue<string | null>() ?? '—',
    },
    {
      accessorKey: 'providerName',
      header: t('common.provider'),
    },
  ]
}
