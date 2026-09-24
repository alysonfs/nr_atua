import { useTranslation } from 'react-i18next'
import { SidebarItem } from './SidebarItem'
import { getNavItems } from './navItems'
import logoBgLight from '../../../../../../../assets/logo-bg-light.svg'

/**
 * Menu lateral esquerdo do Office (desktop, sm+): bloco de marca no topo
 * (mesma altura do header, para alinhar as duas bordas) seguido da
 * navegação. Em telas menores, o MobileMenu assume esse papel.
 */
export function Sidebar() {
  const { t } = useTranslation()
  const items = getNavItems(t)

  return (
    <aside
      aria-label={t('layout.sideMenu')}
      className="sticky top-0 hidden h-screen w-56 shrink-0 overflow-y-auto border-r border-slate-800 bg-slate-950 text-slate-100 sm:block"
    >
      <div className="flex h-16 items-center justify-center border-b border-slate-800 px-4">
        <span className="rounded-md bg-white px-2 py-1 shadow-sm">
          <img src={logoBgLight} alt={t('common.brandAlt')} className="h-6 w-auto" />
        </span>
      </div>
      <nav aria-label={t('layout.mainNavigation')} className="sticky top-16 space-y-1 p-4">
        <p className="px-2 text-xs font-semibold uppercase tracking-wide text-slate-500">
          {t('layout.menu')}
        </p>
        {items.map((item) => (
          <SidebarItem key={item.to} {...item} />
        ))}
      </nav>
    </aside>
  )
}
