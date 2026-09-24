import { HamburgerButton, Remind } from '@icon-park/react'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../../../auth/AuthContext'
import { LanguageSelector } from '../../../../shared/components/LanguageSelector'
import { TrialBadge } from '../Trial'
import { UserMenu } from './UserMenu'

/**
 * Placeholder fake da marca do cliente. Upload real de marca é
 * planejado em RF-019 (docs/requirements/RF-019-upload-marca-cliente.md) e
 * ainda não está implementado — aqui apenas demarcamos a área no layout.
 */
function ClientBrandPlaceholder({ label }: { label: string }) {
  return (
    <div className="hidden h-9 items-center gap-2 rounded-md border border-slate-200 bg-slate-50 px-3 sm:flex">
      <span
        aria-hidden="true"
        className="flex h-6 w-6 items-center justify-center rounded bg-sky-100 text-xs font-semibold text-sky-700"
      >
        NC
      </span>
      <span className="text-sm font-medium text-slate-700">{label}</span>
    </div>
  )
}

interface DashboardHeaderProps {
  /** Disparado ao clicar no botão sanduíche (mobile, sm:hidden). */
  onOpenMobileMenu: () => void
}

/**
 * Header/topbar do Office. Claro e enxuto: marca do cliente (placeholder) e
 * trial à esquerda; notificações, idioma e menu de conta (avatar) à direita.
 * A marca do Atyno ficou no topo do Sidebar (alinhada à mesma altura).
 *
 * Todos os controles interativos do header (bandeira, sino, trial, avatar)
 * seguem a mesma altura (h-9), para manter o ritmo horizontal consistente.
 */
export function DashboardHeader({ onOpenMobileMenu }: DashboardHeaderProps) {
  const { t } = useTranslation()
  const { changeLocale, signOut } = useAuth()

  return (
    <header className="sticky top-0 z-30 border-b border-slate-200 bg-white shadow-sm">
      <div className="mx-auto flex h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6 lg:px-8">
        <div className="flex min-w-0 items-center gap-3">
          <button
            type="button"
            onClick={onOpenMobileMenu}
            aria-label={t('layout.openMenu')}
            className="flex h-9 w-9 items-center justify-center rounded-md text-slate-500 transition hover:bg-slate-100 hover:text-slate-700 sm:hidden"
          >
            <HamburgerButton theme="outline" size={20} />
          </button>
          <ClientBrandPlaceholder label={t('layout.clientName')} />
          <TrialBadge compact className="h-9" />
        </div>

        <div className="flex items-center gap-2 sm:gap-3">
          <span
            role="img"
            aria-label={t('layout.alerts')}
            className="flex h-9 w-9 items-center justify-center rounded-md text-slate-400 transition hover:bg-slate-100 hover:text-slate-600"
          >
            <Remind theme="outline" size={20} />
          </span>
          <LanguageSelector compact onLocaleChange={(locale) => void changeLocale(locale)} />
          <div aria-hidden="true" className="hidden h-6 w-px bg-slate-200 sm:block" />
          <UserMenu onSignOut={() => void signOut()} />
        </div>
      </div>
    </header>
  )
}
