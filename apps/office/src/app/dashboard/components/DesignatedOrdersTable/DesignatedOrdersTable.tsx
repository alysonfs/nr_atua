import { flexRender, getCoreRowModel, useReactTable } from '@tanstack/react-table'
import { useDesignatedServiceOrders } from '../../hooks/useDesignatedServiceOrders'
import { designatedOrdersColumns } from './columns'

/**
 * Seção "OS designadas" do Dashboard: lista, em formato de tabela, as
 * ordens de serviço atualmente com status "Designado".
 *
 * Os dados exibidos são mockados (ver useDesignatedServiceOrders); a
 * integração com o endpoint real de listagem de OS é uma etapa futura.
 */
export function DesignatedOrdersTable() {
  const data = useDesignatedServiceOrders()

  const table = useReactTable({
    data,
    columns: designatedOrdersColumns,
    getCoreRowModel: getCoreRowModel(),
  })

  return (
    <section aria-labelledby="designated-orders-heading" className="space-y-3">
      <h2 id="designated-orders-heading" className="text-sm font-semibold text-slate-700">
        Ordens de serviço designadas
      </h2>

      <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
        <table className="min-w-full divide-y divide-slate-200 text-sm">
          <thead className="bg-slate-50">
            {table.getHeaderGroups().map((headerGroup) => (
              <tr key={headerGroup.id}>
                {headerGroup.headers.map((header) => (
                  <th
                    key={header.id}
                    scope="col"
                    className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wider text-slate-500"
                  >
                    {header.isPlaceholder ? null : flexRender(header.column.columnDef.header, header.getContext())}
                  </th>
                ))}
              </tr>
            ))}
          </thead>

          <tbody className="divide-y divide-slate-100">
            {table.getRowModel().rows.length === 0 ? (
              <tr>
                <td colSpan={designatedOrdersColumns.length} className="px-4 py-6 text-center text-sm text-slate-500">
                  Nenhuma ordem de serviço designada no momento.
                </td>
              </tr>
            ) : (
              table.getRowModel().rows.map((row) => (
                <tr key={row.id} className="hover:bg-slate-50">
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
    </section>
  )
}
