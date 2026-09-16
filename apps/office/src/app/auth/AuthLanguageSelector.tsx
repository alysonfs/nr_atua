import { LanguageSelector } from '../../shared/components/LanguageSelector'
import { useAuth } from './AuthContext'

export function AuthLanguageSelector() {
  const { changeLocale } = useAuth()

  return (
    <div className="absolute right-4 top-4 z-10 sm:right-6 sm:top-6">
      <LanguageSelector compact onLocaleChange={(locale) => void changeLocale(locale)} />
    </div>
  )
}
