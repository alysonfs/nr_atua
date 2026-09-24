import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { Down, Logout, Setting, User } from '@icon-park/react'
import { useTranslation } from 'react-i18next'

interface UserMenuProps {
  onSignOut: () => void
}

/**
 * Menu de conta no header: um único ponto de entrada (avatar) para ações
 * de conta (Configurações, Sair), em vez de ícones soltos espalhados na
 * topbar. Fecha ao clicar fora ou pressionar Escape.
 */
export function UserMenu({ onSignOut }: UserMenuProps) {
  const { t } = useTranslation()
  const [isOpen, setIsOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!isOpen) return

    function handlePointerDown(event: MouseEvent) {
      if (!containerRef.current?.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') setIsOpen(false)
    }

    document.addEventListener('mousedown', handlePointerDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [isOpen])

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        aria-haspopup="menu"
        aria-expanded={isOpen}
        aria-label={t('layout.accountMenuAria')}
        onClick={() => setIsOpen((open) => !open)}
        className="flex h-9 items-center gap-1.5 rounded-md border border-slate-200 bg-white pl-1 pr-2 shadow-sm transition hover:bg-slate-50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-500"
      >
        <span className="flex h-7 w-7 items-center justify-center rounded-full bg-sky-100 text-sky-700">
          <User theme="outline" size={16} />
        </span>
        <Down theme="outline" size={14} className="text-slate-400" />
      </button>

      {isOpen && (
        <div
          role="menu"
          aria-label={t('layout.accountMenuAria')}
          className="absolute right-0 top-full z-50 mt-2 w-48 overflow-hidden rounded-md border border-slate-200 bg-white py-1 shadow-lg"
        >
          <Link
            to="/settings"
            role="menuitem"
            onClick={() => setIsOpen(false)}
            className="flex items-center gap-2 px-3 py-2 text-sm text-slate-700 transition hover:bg-slate-50"
          >
            <Setting theme="outline" size={16} />
            {t('layout.settings')}
          </Link>
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setIsOpen(false)
              onSignOut()
            }}
            className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-red-700 transition hover:bg-red-50"
            aria-label={t('layout.signOutAria')}
          >
            <Logout theme="outline" size={16} />
            {t('layout.signOut')}
          </button>
        </div>
      )}
    </div>
  )
}
