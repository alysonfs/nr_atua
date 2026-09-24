import { Dashboard, Setting } from '@icon-park/react'
import type { TFunction } from 'i18next'
import type { SidebarItemConfig } from './SidebarItem'

/** Itens de navegação compartilhados entre o Sidebar (desktop) e o MobileMenu. */
export function getNavItems(t: TFunction): SidebarItemConfig[] {
  return [
    { label: t('layout.dashboard'), to: '/home', icon: <Dashboard theme="outline" size={18} /> },
    { label: t('layout.settings'), to: '/settings', icon: <Setting theme="outline" size={18} /> },
  ]
}
