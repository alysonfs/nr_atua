import { NavLink } from 'react-router-dom'
import type { ReactNode } from 'react'

export interface SidebarItemConfig {
  label: string
  to: string
  icon: ReactNode
  /** Disparado ao navegar (usado pelo MobileMenu para fechar o overlay). */
  onNavigate?: () => void
  /** Contador exibido ao lado do rótulo (ex.: itens do pool de triagem). Omitido quando `undefined` ou zero. */
  badge?: number
}

/** Item individual de navegação do menu lateral. */
export function SidebarItem({ to, icon, label, onNavigate, badge }: SidebarItemConfig) {
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
      <span className="flex-1">{label}</span>
      {Boolean(badge) && (
        <span className="inline-flex min-w-5 items-center justify-center rounded-full bg-sky-600 px-1.5 py-0.5 text-xs font-semibold text-white">
          {badge}
        </span>
      )}
    </NavLink>
  )
}
