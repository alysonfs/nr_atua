import { flexRender, type Table } from '@tanstack/react-table'
import type { ReactNode } from 'react'

interface DataTableProps<TData> {
  /** Instância do TanStack Table já configurada (colunas, dados, row model). */
  table: Table<TData>
  /** Mensagem exibida quando não há linhas. */
  emptyMessage: string
  /** Conteúdo opcional renderizado abaixo da tabela (ex.: paginação). */
  footer?: ReactNode
}

/**
 * Wrapper reutilizável sobre `@tanstack/react-table`: header sticky, zebra
 * striping e hover state padronizados para as tabelas do Office.
 */
export function DataTable<TData>({ table, emptyMessage, footer }: DataTableProps<TData>) {
  const columnCount = table.getAllLeafColumns().length
  const rows = table.getRowModel().rows

  return (
    <div className="space-y-3">
      <div className="max-h-112 overflow-auto rounded-lg border border-slate-200 bg-white shadow-sm">
        <table className="min-w-full divide-y divide-slate-200 text-sm">
          <thead className="sticky top-0 z-10 bg-slate-50">
            {table.getHeaderGroups().map((headerGroup) => (
              <tr key={headerGroup.id}>
                {headerGroup.headers.map((header) => (
                  <th
                    key={header.id}
                    scope="col"
                    className="bg-slate-50 px-4 py-3 text-left text-xs font-semibold uppercase tracking-wider text-slate-500"
                  >
                    {header.isPlaceholder ? null : flexRender(header.column.columnDef.header, header.getContext())}
                  </th>
                ))}
              </tr>
            ))}
          </thead>

          <tbody className="divide-y divide-slate-100">
            {rows.length === 0 ? (
              <tr>
                <td colSpan={columnCount} className="px-4 py-6 text-center text-sm text-slate-500">
                  {emptyMessage}
                </td>
              </tr>
            ) : (
              rows.map((row) => (
                <tr key={row.id} className="odd:bg-slate-50/60 hover:bg-slate-100">
                  {row.getVisibleCells().map((cell) => (
                    <td key={cell.id} className="whitespace-nowrap px-4 py-3 text-slate-700">
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {footer}
    </div>
  )
}
