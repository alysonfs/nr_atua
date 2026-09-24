import { Close } from '@icon-park/react'
import { useTranslation } from 'react-i18next'
import { SidebarItem } from './SidebarItem'
import { getNavItems } from './navItems'
import logoBgLight from '../../../../../../../assets/logo-bg-light.svg'

interface MobileMenuProps {
  /** Se o overlay está aberto. */
  open: boolean
  /** Disparado ao fechar (botão "X" ou navegação para uma rota). */
  onClose: () => void
}

/**
 * Menu de navegação mobile (sm:hidden): overlay em tela cheia que desliza
 * de cima para baixo, substituindo o Sidebar (oculto em telas pequenas).
 */
export function MobileMenu({ open, onClose }: MobileMenuProps) {
  const { t } = useTranslation()
  const items = getNavItems(t)

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-hidden={!open}
      aria-label={t('layout.sideMenu')}
      className={`fixed inset-0 z-50 flex flex-col bg-slate-950 text-slate-100 transition-transform duration-300 ease-out sm:hidden ${
        open ? 'translate-y-0' : 'pointer-events-none -translate-y-full'
      }`}
    >
      <div className="grid h-16 grid-cols-[1fr_auto_1fr] items-center border-b border-slate-800 px-4">
        <span aria-hidden="true" />
        <span className="justify-self-center rounded-md bg-white px-2 py-1 shadow-sm">
          <img src={logoBgLight} alt={t('common.brandAlt')} className="h-6 w-auto" />
        </span>
        <button
          type="button"
          onClick={onClose}
          aria-label={t('layout.closeMenu')}
          className="flex h-9 w-9 items-center justify-center justify-self-end rounded-md text-slate-300 transition hover:bg-slate-900 hover:text-white"
        >
          <Close theme="outline" size={20} />
        </button>
      </div>
      <nav aria-label={t('layout.mainNavigation')} className="space-y-1 p-4">
        <p className="px-2 text-xs font-semibold uppercase tracking-wide text-slate-500">
          {t('layout.menu')}
        </p>
        {items.map((item) => (
          <SidebarItem key={item.to} {...item} onNavigate={onClose} />
        ))}
      </nav>
    </div>
  )
}
