import { Link } from 'react-router-dom'
import { Setting, Remind } from '@icon-park/react'
import logoBgLight from '../../../../../../../assets/logo_bg_light.svg'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

/**
 * Placeholder fake da marca do cliente. Upload real de marca é
 * planejado em RF-019 (docs/requirements/RF-019-upload-marca-cliente.md) e
 * ainda não está implementado — aqui apenas demarcamos a área no layout.
 */
function ClientBrandPlaceholder({ label }: { label: string }) {
  return (
    <div className="hidden items-center gap-2 rounded-md border border-white/10 bg-white/8 px-3 py-1.5 sm:flex">
      <span
        aria-hidden="true"
        className="flex h-7 w-7 items-center justify-center rounded bg-sky-400 text-xs font-semibold text-slate-950"
      >
        NC
      </span>
      <span className="text-sm font-medium text-slate-100">{label}</span>
    </div>
  )
}

/**
 * Ícone puramente decorativo, sem ação. Usado para demarcar área de
 * alertas no Dashboard mínimo, antes de definirmos o menu real.
 */
function DecorativeIcon({ icon, label }: { icon: ReactNode; label: string }) {
  return (
    <span
      role="img"
      aria-label={label}
      className="flex h-9 w-9 items-center justify-center rounded-md border border-white/10 bg-white/8 text-slate-200"
    >
      {icon}
    </span>
  )
}

/**
 * Header/topbar do Office. Mostra a marca do Atyno, marca do cliente
 * (placeholder), um ícone de alertas ainda decorativo e o ícone de
 * Configurações, que navega para a rota /settings (Configurações).
 * O menu lateral esquerdo (Sidebar) é renderizado pelo AppLayout.
 */
interface DashboardHeaderProps {
  actions?: ReactNode
}

export function DashboardHeader({ actions }: DashboardHeaderProps) {
  const { t } = useTranslation()
  return (
    <header className="sticky top-0 z-40 border-b border-slate-800 bg-slate-950 text-white shadow-sm">
      <div className="mx-auto flex h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6 lg:px-8">
        <div className="flex min-w-0 items-center gap-4">
          <span className="rounded-md bg-white px-2.5 py-1.5 shadow-sm">
            <img src={logoBgLight} alt="Atyno" className="h-7 w-auto" />
          </span>
          <div className="hidden min-w-0 border-l border-white/10 pl-4 md:block">
            <p className="text-xs font-medium uppercase text-slate-400">{t('layout.office')}</p>
            <p className="truncate text-sm font-semibold text-slate-100">{t('layout.subtitle')}</p>
          </div>
          <ClientBrandPlaceholder label={t('layout.clientName')} />
        </div>

        <div className="flex items-center gap-2 sm:gap-3">
          <DecorativeIcon
            icon={<Remind theme="outline" size={20} fill="#e2e8f0" />}
            label={t('layout.alerts')}
          />
          <Link
            to="/settings"
            aria-label={t('layout.settings')}
            className="flex h-9 w-9 items-center justify-center rounded-md border border-white/10 bg-white/8 text-slate-200 transition hover:bg-white/14 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-400"
          >
            <Setting theme="outline" size={20} fill="#e2e8f0" />
          </Link>
          {actions}
        </div>
      </div>
    </header>
  )
}
