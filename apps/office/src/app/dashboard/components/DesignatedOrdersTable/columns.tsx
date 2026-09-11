import type { ColumnDef } from '@tanstack/react-table'
import type { DesignatedServiceOrder } from '../../hooks/useDesignatedServiceOrders'

/**
 * Formata uma data ISO 8601 no padrão brasileiro (dd/mm/aaaa hh:mm).
 */
function formatDateTime(isoDate: string): string {
  return new Date(isoDate).toLocaleString('pt-BR', {
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
export const designatedOrdersColumns: ColumnDef<DesignatedServiceOrder>[] = [
  {
    accessorKey: 'id',
    header: 'OS',
  },
  {
    accessorKey: 'providerName',
    header: 'Provedor',
  },
  {
    accessorKey: 'status',
    header: 'Status',
  },
  {
    accessorKey: 'createdAt',
    header: 'Criada em',
    cell: (info) => formatDateTime(info.getValue<string>()),
  },
  {
    accessorKey: 'updatedAt',
    header: 'Atualizada em',
    cell: (info) => formatDateTime(info.getValue<string>()),
  },
]
