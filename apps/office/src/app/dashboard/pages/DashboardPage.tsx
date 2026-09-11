import { DesignatedOrdersTable } from '../components/DesignatedOrdersTable'
import { SummaryMonth } from '../components/SummaryMonth'

/**
 * Dashboard: home pós-login do Office. A integração com a API real de
 * sumário mensal e de listagem de OS será adicionada em etapa futura.
 */
export function DashboardPage() {
  return (
    <div className="mx-auto w-full max-w-7xl space-y-5 px-4 py-6 sm:px-6 lg:px-8">
      <div className="border-b border-slate-200 pb-5">
        <p className="text-xs font-semibold uppercase text-sky-700">Atyno</p>
        <h1 className="mt-1 text-2xl font-semibold text-slate-950 sm:text-3xl">Dashboard</h1>
        <p className="mt-1 max-w-2xl text-sm text-slate-600">
          Visão geral das ordens de serviço da sua operação.
        </p>
      </div>

      <SummaryMonth />

      <DesignatedOrdersTable />
    </div>
  )
}
