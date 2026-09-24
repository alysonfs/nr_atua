import { NavLink } from 'react-router-dom'
import type { ReactNode } from 'react'

export interface SidebarItemConfig {
  label: string
  to: string
  icon: ReactNode
  /** Disparado ao navegar (usado pelo MobileMenu para fechar o overlay). */
  onNavigate?: () => void
}

/** Item individual de navegação do menu lateral. */
export function SidebarItem({ to, icon, label, onNavigate }: SidebarItemConfig) {
  return (
    <NavLink
      to={to}
      onClick={onNavigate}
      className={({ isActive }) =>
        `flex items-center gap-2 rounded-md px-2 py-2 text-sm font-medium transition ${
          isActive ? 'bg-slate-900 text-white' : 'text-slate-400 hover:bg-slate-900/60 hover:text-slate-100'
        }`
      }
    >
      {icon}
      {label}
    </NavLink>
  )
}
