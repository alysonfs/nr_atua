import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { addLocaleToUrl, OFFICE_SIGNIN_URL, OFFICE_SIGNUP_URL } from './constants'
import {
  DEFAULT_LOCALE,
  hasExplicitLocaleChoice,
  normalizeLocale,
  persistLocale,
  type SupportedLocale,
} from './i18n'
import { LanguageSelector } from './LanguageSelector'
import { AudienceSection } from './sections/AudienceSection'
import { FinalCtaSection } from './sections/FinalCtaSection'
import { Footer } from './sections/Footer'
import { Hero } from './sections/Hero'
import { PlatformSection } from './sections/PlatformSection'
import { StepsSection } from './sections/StepsSection'
import { TrustSection } from './sections/TrustSection'

function App() {
  const { i18n, t } = useTranslation()
  const [explicitLocaleChoice, setExplicitLocaleChoice] = useState(
    hasExplicitLocaleChoice,
  )
  const locale = normalizeLocale(i18n.resolvedLanguage) ?? DEFAULT_LOCALE

  const officeUrls = useMemo(() => {
    const localeForOffice = explicitLocaleChoice ? locale : undefined

    return {
      signIn: addLocaleToUrl(OFFICE_SIGNIN_URL, localeForOffice),
      signUp: addLocaleToUrl(OFFICE_SIGNUP_URL, localeForOffice),
    }
  }, [explicitLocaleChoice, locale])

  useEffect(() => {
    document.documentElement.lang = locale
    document.title = t('metadata.title')

    const description = document.querySelector<HTMLMetaElement>(
      'meta[name="description"]',
    )
    description?.setAttribute('content', t('metadata.description'))
  }, [locale, t])

  const handleLocaleChange = async (nextLocale: SupportedLocale) => {
    persistLocale(nextLocale, true)
    await i18n.changeLanguage(nextLocale)
    setExplicitLocaleChoice(true)
  }

  return (
    <>
      <Hero
        languageSelector={
          <LanguageSelector
            locale={locale}
            onLocaleChange={(nextLocale) => void handleLocaleChange(nextLocale)}
          />
        }
        signInUrl={officeUrls.signIn}
        signUpUrl={officeUrls.signUp}
      />
      <main>
        <PlatformSection />
        <AudienceSection />
        <TrustSection />
        <StepsSection />
        <FinalCtaSection
          signInUrl={officeUrls.signIn}
          signUpUrl={officeUrls.signUp}
        />
      </main>
      <Footer signInUrl={officeUrls.signIn} signUpUrl={officeUrls.signUp} />
    </>
  )
}

export default App
