import { flexRender, getCoreRowModel, useReactTable } from '@tanstack/react-table'
import { useDesignatedServiceOrders } from '../../hooks/useDesignatedServiceOrders'
import { designatedOrdersColumns } from './columns'

interface DesignatedOrdersTableProps {
  /** Tenant ativo do usuário (ver useMyTenants().defaultTenantId). */
  tenantId: string | null
}

/**
 * Seção "OS designadas" do Dashboard: lista, em formato de tabela, as
 * ordens de serviço atualmente com status "Designado".
 *
 * Integra com GET /api/tenants/{tenantId}/work-orders?status=Designado
 * (ver useDesignatedServiceOrders).
 */
export function DesignatedOrdersTable({ tenantId }: DesignatedOrdersTableProps) {
  const { orders, isLoading, isError } = useDesignatedServiceOrders(tenantId)

  const table = useReactTable({
    data: orders,
    columns: designatedOrdersColumns,
    getCoreRowModel: getCoreRowModel(),
  })

  return (
    <section aria-labelledby="designated-orders-heading" className="space-y-3">
      <h2 id="designated-orders-heading" className="text-sm font-semibold text-slate-700">
        Ordens de serviço designadas
      </h2>

      {isLoading && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          Carregando ordens de serviço designadas...
        </p>
      )}

      {!isLoading && isError && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          Não foi possível carregar as ordens de serviço designadas. Tente novamente mais tarde.
        </p>
      )}

      {!isLoading && !isError && (
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
      )}
    </section>
  )
}
