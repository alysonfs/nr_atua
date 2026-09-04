import logoBgLight from '../../../../../../../assets/logo_bg_light.svg'
import iconSettings from '../../../../../../../assets/icon/icon-settings.svg'
import iconAlert from '../../../../../../../assets/icon/icon-alert.svg'
import type { ReactNode } from 'react'

/**
 * Placeholder fake da marca do cliente. Upload real de marca é
 * planejado em RF-019 (docs/requirements/RF-019-upload-marca-cliente.md) e
 * ainda não está implementado — aqui apenas demarcamos a área no layout.
 */
function ClientBrandPlaceholder() {
  return (
    <div className="hidden items-center gap-2 rounded-md border border-white/10 bg-white/8 px-3 py-1.5 sm:flex">
      <span
        aria-hidden="true"
        className="flex h-7 w-7 items-center justify-center rounded bg-sky-400 text-xs font-semibold text-slate-950"
      >
        NC
      </span>
      <span className="text-sm font-medium text-slate-100">Nome do Cliente</span>
    </div>
  )
}

/**
 * Ícone puramente decorativo, sem ação. Usado para demarcar área de
 * settings e alertas no Dashboard mínimo, antes de definirmos o menu real.
 */
function DecorativeIcon({ src, label }: { src: string; label: string }) {
  return (
    <span
      role="img"
      aria-label={label}
      className="flex h-9 w-9 items-center justify-center rounded-md border border-white/10 bg-white/8 text-slate-200"
    >
      <img src={src} alt="" aria-hidden="true" className="h-5 w-5 invert" />
    </span>
  )
}

/**
 * Header/topbar do Dashboard mínimo do Office. Objetivo é apenas demarcar
 * área de menu: marca do ATUA, marca do cliente (placeholder), e ícones de
 * settings/alerta sem ação. Nenhum item aqui é clicável — a definição do
 * menu real e das ações fica para uma próxima etapa.
 */
interface DashboardHeaderProps {
  actions?: ReactNode
}

export function DashboardHeader({ actions }: DashboardHeaderProps) {
  return (
    <header className="sticky top-0 z-40 border-b border-slate-800 bg-slate-950 text-white shadow-sm">
      <div className="mx-auto flex h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6 lg:px-8">
        <div className="flex min-w-0 items-center gap-4">
          <span className="rounded-md bg-white px-2.5 py-1.5 shadow-sm">
            <img src={logoBgLight} alt="ATUA" className="h-7 w-auto" />
          </span>
          <div className="hidden min-w-0 border-l border-white/10 pl-4 md:block">
            <p className="text-xs font-medium uppercase text-slate-400">Office</p>
            <p className="truncate text-sm font-semibold text-slate-100">Operações e integrações</p>
          </div>
          <ClientBrandPlaceholder />
        </div>

        <div className="flex items-center gap-2 sm:gap-3">
          <DecorativeIcon src={iconAlert} label="Alertas (área reservada)" />
          <DecorativeIcon src={iconSettings} label="Configurações (área reservada)" />
          {actions}
        </div>
      </div>
    </header>
  )
}
