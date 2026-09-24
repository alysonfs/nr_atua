import { useState } from 'react'
import { Outlet } from 'react-router-dom'
import { DashboardHeader } from './DashboardHeader'
import { Sidebar } from './Sidebar'
import { MobileMenu } from './MobileMenu'
import { Breadcrumbs } from './Breadcrumbs'
import { useAuth } from '../../../auth/AuthContext'
import { useTranslation } from 'react-i18next'

/**
 * Layout raiz das rotas protegidas do Office: Sidebar como coluna de altura
 * total à esquerda (o bloco de marca do Sidebar fica na mesma linha do
 * DashboardHeader, evitando duas faixas de topo empilhadas) e, à direita,
 * o DashboardHeader fixo seguido da rota ativa (dashboard ou settings).
 */
export function AppLayout() {
  const { localeSyncError, retryLocaleSync } = useAuth()
  const { t } = useTranslation()
  const [isMobileMenuOpen, setMobileMenuOpen] = useState(false)

  return (
    <div className="flex min-h-screen bg-slate-50 text-slate-950">
      <Sidebar />
      <MobileMenu open={isMobileMenuOpen} onClose={() => setMobileMenuOpen(false)} />

      <div className="flex min-w-0 flex-1 flex-col">
        <DashboardHeader onOpenMobileMenu={() => setMobileMenuOpen(true)} />

        <main className="min-w-0 flex-1 overflow-y-auto p-4 sm:p-6 lg:p-8">
          <div className="mx-auto grid w-full max-w-7xl auto-rows-min grid-cols-12 gap-4">
            <div className="col-span-12">
              <Breadcrumbs />
              {localeSyncError && (
                <button
                  type="button"
                  role="alert"
                  onClick={() => void retryLocaleSync()}
                  className="mt-2 w-full rounded-md border border-amber-200 bg-amber-50 px-4 py-2 text-left text-sm text-amber-900"
                >
                  {t('language.syncError')}
                </button>
              )}
            </div>
            <div className="col-span-12">
              <Outlet />
            </div>
          </div>
        </main>
      </div>
    </div>
  )
}
