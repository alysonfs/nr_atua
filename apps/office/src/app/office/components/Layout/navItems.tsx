import { Dashboard, FileText, Inbox, Setting } from '@icon-park/react'
import type { TFunction } from 'i18next'
import type { SidebarItemConfig } from './SidebarItem'

/**
 * Itens de navegação compartilhados entre o Sidebar (desktop) e o
 * MobileMenu. `triagePoolCount`, quando informado, é exibido como contador
 * no item "Pool de triagem" (épico `docs/features/triagem-pool-operador.md`,
 * seção 2 — "Contador de itens do pool visível na navegação").
 */
export function getNavItems(t: TFunction, triagePoolCount?: number): SidebarItemConfig[] {
  return [
    { label: t('layout.dashboard'), to: '/home', icon: <Dashboard theme="outline" size={18} /> },
    { label: t('layout.serviceOrders'), to: '/service-orders', icon: <FileText theme="outline" size={18} /> },
    {
      label: t('layout.triagePool'),
      to: '/triage-pool',
      icon: <Inbox theme="outline" size={18} />,
      badge: triagePoolCount,
    },
    { label: t('layout.settings'), to: '/settings', icon: <Setting theme="outline" size={18} /> },
  ]
}
