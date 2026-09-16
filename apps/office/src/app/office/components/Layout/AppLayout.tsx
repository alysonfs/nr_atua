import { Outlet } from 'react-router-dom'
import { DashboardHeader } from './DashboardHeader'
import { Sidebar } from './Sidebar'
import { TrialBadge } from '../Trial'
import { useAuth } from '../../../auth/AuthContext'

/**
 * Layout raiz das rotas protegidas do Office: mantém o DashboardHeader
 * sempre visível no topo, o Sidebar fixo à esquerda (ainda sem itens de
 * navegação definidos) e renderiza a rota ativa (dashboard ou settings)
 * no miolo, ao lado do Sidebar.
 */
export function AppLayout() {
  const { signOut } = useAuth()

  return (
    <div className="min-h-screen bg-slate-50 text-slate-950">
      <DashboardHeader
        actions={(
          <>
            <TrialBadge compact />
            <button
              type="button"
              onClick={() => void signOut()}
              className="rounded-md border border-white/10 bg-white/8 px-3 py-2 text-sm font-semibold text-slate-100 transition hover:bg-white/14 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-400"
              aria-label="Sair da conta"
            >
              Sair
            </button>
          </>
        )}
      />

      <div className="flex min-h-[calc(100vh-4rem)]">
        <Sidebar />
        <main className="min-w-0 flex-1">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
