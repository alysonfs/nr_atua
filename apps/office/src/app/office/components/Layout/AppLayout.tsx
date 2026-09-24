import { Outlet } from 'react-router-dom'
import { DashboardHeader } from './DashboardHeader'
import { Sidebar } from './Sidebar'
import { Breadcrumbs } from './Breadcrumbs'
import { TrialBadge } from '../Trial'
import { useAuth } from '../../../auth/AuthContext'
import { useTranslation } from 'react-i18next'
import { LanguageSelector } from '../../../../shared/components/LanguageSelector'

/**
 * Layout raiz das rotas protegidas do Office: mantém o DashboardHeader
 * sempre visível no topo, o Sidebar fixo à esquerda (ainda sem itens de
 * navegação definidos) e renderiza a rota ativa (dashboard ou settings)
 * no miolo, ao lado do Sidebar.
 */
export function AppLayout() {
  const { changeLocale, localeSyncError, retryLocaleSync, signOut } = useAuth()
  const { t } = useTranslation()

  return (
    <div className="min-h-screen bg-slate-50 text-slate-950">
      <DashboardHeader
        actions={(
          <>
            <LanguageSelector compact onLocaleChange={(locale) => void changeLocale(locale)} />
            <TrialBadge compact />
            <button
              type="button"
              onClick={() => void signOut()}
              className="rounded-md border border-white/10 bg-white/8 px-3 py-2 text-sm font-semibold text-slate-100 transition hover:bg-white/14 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-400"
              aria-label={t('layout.signOutAria')}
            >
              {t('layout.signOut')}
            </button>
          </>
        )}
      />

      <div className="flex min-h-[calc(100vh-4rem)]">
        <Sidebar />
        <main className="min-w-0 flex-1">
          <Breadcrumbs />
          {localeSyncError && (
            <button
              type="button"
              role="alert"
              onClick={() => void retryLocaleSync()}
              className="w-full border-b border-amber-200 bg-amber-50 px-4 py-2 text-left text-sm text-amber-900"
            >
              {t('language.syncError')}
            </button>
          )}
          <Outlet />
        </main>
      </div>
    </div>
  )
}
