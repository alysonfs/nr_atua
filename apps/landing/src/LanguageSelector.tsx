import { useTranslation } from 'react-i18next'
import { languages, type SupportedLocale } from './i18n'

type LanguageSelectorProps = {
  locale: SupportedLocale
  onLocaleChange: (locale: SupportedLocale) => void
}

export function LanguageSelector({
  locale,
  onLocaleChange,
}: LanguageSelectorProps) {
  const { t } = useTranslation()

  return (
    <div
      role="group"
      aria-label={t('languageSelector.label')}
      className="flex flex-wrap justify-end gap-1 rounded-xl border border-slate-200 bg-white p-1 shadow-sm"
    >
      {languages.map((language) => {
        const languageName = t(language.nameKey)
        const isSelected = language.locale === locale

        return (
          <button
            key={language.locale}
            type="button"
            aria-label={t('languageSelector.optionLabel', { language: languageName })}
            aria-pressed={isSelected}
            className={`flex min-h-10 items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 ${
              isSelected
                ? 'bg-blue-50 text-blue-700'
                : 'text-slate-600 hover:bg-slate-100 hover:text-slate-950'
            }`}
            onClick={() => onLocaleChange(language.locale)}
          >
            <span aria-hidden="true" className="text-lg leading-none">
              {language.flag}
            </span>
            <span className="hidden md:inline">{languageName}</span>
          </button>
        )
      })}
    </div>
  )
}
