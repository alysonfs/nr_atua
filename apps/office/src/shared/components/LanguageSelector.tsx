import { useTranslation } from 'react-i18next'
import {
  DEFAULT_LOCALE,
  languages,
  normalizeLocale,
  type SupportedLocale,
} from '../../i18n'

interface LanguageSelectorProps {
  onLocaleChange: (locale: SupportedLocale) => void
  compact?: boolean
}

export function LanguageSelector({ onLocaleChange, compact = false }: LanguageSelectorProps) {
  const { i18n, t } = useTranslation()
  const locale = normalizeLocale(i18n.resolvedLanguage) ?? DEFAULT_LOCALE

  return (
    <div
      role="group"
      aria-label={t('language.select')}
      className="flex items-center gap-1 rounded-md border border-slate-200 bg-white p-1 shadow-sm"
    >
      {languages.map((language) => {
        const name = t(language.nameKey)
        const selected = locale === language.locale

        return (
          <button
            key={language.locale}
            type="button"
            aria-label={t('language.option', { language: name })}
            aria-pressed={selected}
            title={name}
            onClick={() => onLocaleChange(language.locale)}
            className={`flex min-h-8 items-center gap-1.5 rounded px-2 py-1 text-sm font-medium transition focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-500 ${
              selected
                ? 'bg-sky-50 text-sky-700'
                : 'text-slate-600 hover:bg-slate-100 hover:text-slate-950'
            }`}
          >
            <span aria-hidden="true" className="text-base leading-none">{language.flag}</span>
            {!compact && <span className="hidden xl:inline">{name}</span>}
          </button>
        )
      })}
    </div>
  )
}
