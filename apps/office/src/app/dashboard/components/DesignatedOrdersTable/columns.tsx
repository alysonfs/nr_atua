import type { ColumnDef } from '@tanstack/react-table'
import type { TFunction } from 'i18next'
import type { DesignatedServiceOrder } from '../../hooks/useDesignatedServiceOrders'

/**
 * Formata uma data ISO 8601 no padrão brasileiro (dd/mm/aaaa hh:mm).
 */
function formatDateTime(isoDate: string, locale: string): string {
  return new Date(isoDate).toLocaleString(locale, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

/**
 * Definição das colunas da tabela de OS designadas, usada pelo
 * `@tanstack/react-table` via `useReactTable`.
 */
export function createDesignatedOrdersColumns(
  t: TFunction,
  locale: string,
): ColumnDef<DesignatedServiceOrder>[] {
  return [
  {
    accessorKey: 'id',
    header: 'OS',
  },
  {
    accessorKey: 'providerId',
    header: t('common.providerOrderNumber'),
  },
  {
    accessorKey: 'status',
    header: t('common.status'),
  },
  {
    accessorKey: 'createdAt',
    header: t('common.createdAt'),
    cell: (info) => formatDateTime(info.getValue<string>(), locale),
  },
  {
    accessorKey: 'updatedAt',
    header: t('common.updatedAt'),
    cell: (info) => formatDateTime(info.getValue<string>(), locale),
  },
  ]
}
