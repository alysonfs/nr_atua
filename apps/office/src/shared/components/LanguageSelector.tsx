import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Down } from '@icon-park/react'
import {
  DEFAULT_LOCALE,
  languages,
  normalizeLocale,
  type SupportedLocale,
} from '../../i18n'

interface LanguageSelectorProps {
  onLocaleChange: (locale: SupportedLocale) => void
  /** Quando true, o gatilho mostra só a bandeira ativa (sem o nome do idioma). */
  compact?: boolean
}

/**
 * Seletor de idioma como um dropdown: o gatilho mostra apenas a bandeira do
 * idioma ativo (+ seta), e o menu com as demais opções só aparece ao
 * expandir. Fecha ao selecionar, clicar fora ou pressionar Escape.
 */
export function LanguageSelector({ onLocaleChange, compact = false }: LanguageSelectorProps) {
  const { i18n, t } = useTranslation()
  const locale = normalizeLocale(i18n.resolvedLanguage) ?? DEFAULT_LOCALE
  const activeLanguage = languages.find((language) => language.locale === locale) ?? languages[0]

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
        aria-label={t('language.select')}
        onClick={() => setIsOpen((open) => !open)}
        className="flex h-9 items-center gap-1.5 rounded-md border border-slate-200 bg-white pl-2 pr-1.5 text-sm font-medium text-slate-600 shadow-sm transition hover:bg-slate-50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-500"
      >
        <span aria-hidden="true" className="text-base leading-none">{activeLanguage.flag}</span>
        {!compact && <span className="hidden xl:inline">{t(activeLanguage.nameKey)}</span>}
        <Down theme="outline" size={14} className="text-slate-400" />
      </button>

      {isOpen && (
        <div
          role="menu"
          aria-label={t('language.select')}
          className="absolute right-0 top-full z-50 mt-2 w-40 overflow-hidden rounded-md border border-slate-200 bg-white py-1 shadow-lg"
        >
          {languages.map((language) => {
            const name = t(language.nameKey)
            const selected = locale === language.locale

            return (
              <button
                key={language.locale}
                type="button"
                role="menuitem"
                aria-label={t('language.option', { language: name })}
                aria-pressed={selected}
                onClick={() => {
                  setIsOpen(false)
                  onLocaleChange(language.locale)
                }}
                className={`flex w-full items-center gap-2 px-3 py-2 text-left text-sm transition ${
                  selected ? 'bg-sky-50 text-sky-700' : 'text-slate-700 hover:bg-slate-50'
                }`}
              >
                <span aria-hidden="true" className="text-base leading-none">{language.flag}</span>
                {name}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
