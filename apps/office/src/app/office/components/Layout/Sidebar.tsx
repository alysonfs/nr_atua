import { NavLink } from 'react-router-dom'
import { Dashboard } from '@icon-park/react'
import type { ReactNode } from 'react'

interface SidebarItem {
  label: string
  to: string
  icon: ReactNode
}

/**
 * Itens de navegação do menu lateral. Por ora, apenas o Dashboard
 * (rota /home) está definido — os demais itens são uma decisão de
 * produto que será feita em uma próxima etapa.
 */
const SIDEBAR_ITEMS: SidebarItem[] = [
  { label: 'Dashboard', to: '/home', icon: <Dashboard theme="outline" size={18} /> },
]

/**
 * Menu lateral esquerdo do Office, na mesma paleta escura do header.
 */
export function Sidebar() {
  return (
    <aside
      aria-label="Menu lateral"
      className="hidden w-56 shrink-0 border-r border-slate-800 bg-slate-950 text-slate-100 sm:block"
    >
      <nav aria-label="Navegação principal" className="sticky top-16 space-y-1 p-4">
        <p className="px-2 text-xs font-semibold uppercase tracking-wide text-slate-500">
          Menu
        </p>
        {SIDEBAR_ITEMS.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) =>
              `flex items-center gap-2 rounded-md px-2 py-2 text-sm font-medium transition ${
                isActive
                  ? 'bg-slate-900 text-white'
                  : 'text-slate-400 hover:bg-slate-900/60 hover:text-slate-100'
              }`
            }
          >
            {item.icon}
            {item.label}
          </NavLink>
        ))}
      </nav>
    </aside>
  )
}
