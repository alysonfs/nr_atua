import { DesignatedOrdersTable } from '../components/DesignatedOrdersTable'
import { SummaryMonth } from '../components/SummaryMonth'
import { useMyTenants } from '../../office/hooks/useTenants'

/**
 * Dashboard: home pós-login do Office.
 *
 * O tenant ativo é resolvido do mesmo jeito que em SettingsPage.tsx
 * (useMyTenants().defaultTenantId): o Dashboard não implementa seleção
 * explícita de tenant — se o usuário tiver múltiplos tenants e nenhum
 * default, os cards/tabela simplesmente não disparam requisição
 * (tenantId null) até que a seleção seja resolvida em outro ponto do app.
 */
export function DashboardPage() {
  const { defaultTenantId, isLoading, isError } = useMyTenants()

  return (
    <div className="mx-auto w-full max-w-7xl space-y-5 px-4 py-6 sm:px-6 lg:px-8">
      <div className="border-b border-slate-200 pb-5">
        <p className="text-xs font-semibold uppercase text-sky-700">Atyno</p>
        <h1 className="mt-1 text-2xl font-semibold text-slate-950 sm:text-3xl">Dashboard</h1>
        <p className="mt-1 max-w-2xl text-sm text-slate-600">
          Visão geral das ordens de serviço da sua operação.
        </p>
      </div>

      {isLoading && (
        <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
          Carregando dados da sua empresa...
        </p>
      )}

      {!isLoading && isError && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
          Não foi possível carregar os dados da sua empresa. Tente novamente mais tarde.
        </p>
      )}

      {!isLoading && !isError && (
        <>
          <SummaryMonth tenantId={defaultTenantId} />
          <DesignatedOrdersTable tenantId={defaultTenantId} />
        </>
      )}
    </div>
  )
}
