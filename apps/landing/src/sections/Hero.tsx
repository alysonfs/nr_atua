import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import logoBgLight from '../../../../assets/logo_bg_light.svg'

type HeroProps = {
  languageSelector: ReactNode
  signInUrl: string
  signUpUrl: string
}

export function Hero({ languageSelector, signInUrl, signUpUrl }: HeroProps) {
  const { t } = useTranslation()
  const badges = [
    t('hero.badges.unifiedPlatform'),
    t('hero.badges.connectedData'),
    t('hero.badges.multipleProviders'),
  ]

  return (
    <header className="overflow-hidden bg-slate-50">
      <div className="mx-auto flex w-full max-w-6xl justify-end px-6 pt-5 lg:px-10">
        {languageSelector}
      </div>
      <div className="mx-auto grid min-h-[78vh] w-full max-w-6xl gap-12 px-6 py-10 sm:py-16 lg:grid-cols-[1.05fr_0.95fr] lg:items-center lg:px-10">
        <div>
          <img
            src={logoBgLight}
            alt={t('common.logoAlt')}
            className="h-auto w-36 sm:w-44"
          />
          <p className="mt-10 text-xs font-semibold uppercase tracking-[0.2em] text-blue-600">
            {t('hero.eyebrow')}
          </p>
          <h1 className="mt-4 max-w-3xl text-4xl font-bold tracking-[-0.03em] text-slate-950 sm:text-5xl lg:text-6xl">
            {t('hero.headline')}
          </h1>
          <p className="mt-6 max-w-2xl text-lg leading-8 text-slate-600 sm:text-xl">
            {t('hero.subheadline')}
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            <a href={signUpUrl} className="btn btn-primary btn-lg bg-linear-to-br from-blue-500 to-blue-600">
              {t('common.actions.signUp')}
            </a>
            <a href="#como-funciona" className="btn btn-outline btn-lg border-slate-900 text-slate-900">
              {t('common.actions.seeHowItWorks')}
            </a>
            <a href={signInUrl} className="btn btn-ghost btn-lg text-slate-600">
              {t('common.actions.signIn')}
            </a>
          </div>
          <ul className="mt-8 flex flex-wrap gap-3" aria-label={t('hero.badgesLabel')}>
            {badges.map((badge) => (
              <li
                key={badge}
                className="rounded-full border border-blue-100 bg-blue-50 px-4 py-2 text-xs font-semibold uppercase tracking-[0.12em] text-blue-700"
              >
                {badge}
              </li>
            ))}
          </ul>
        </div>

        <div className="relative">
          <div className="absolute -right-24 -top-20 h-72 w-72 rounded-full bg-blue-100 blur-3xl" />
          <div className="relative rounded-3xl border border-slate-200 bg-white p-6 shadow-[0_24px_80px_rgba(15,23,42,0.10)]">
            <div className="rounded-2xl bg-slate-950 p-6 text-white">
              <p className="text-sm font-semibold uppercase tracking-[0.16em] text-blue-300">
                {t('hero.diagram.title')}
              </p>
              <div className="mt-8 grid gap-4">
                <div className="rounded-2xl border border-white/10 bg-white/10 p-4">
                  <p className="text-sm text-slate-300">{t('hero.diagram.inputLabel')}</p>
                  <p className="mt-2 text-2xl font-semibold">{t('hero.diagram.inputValue')}</p>
                </div>
                <div className="flex justify-center">
                  <span className="h-10 border-l-2 border-dashed border-blue-400" aria-hidden="true" />
                </div>
                <div className="rounded-2xl border border-blue-400/40 bg-blue-500/15 p-4">
                  <p className="text-sm text-blue-200">{t('hero.diagram.layerLabel')}</p>
                  <p className="mt-2 text-2xl font-semibold">{t('hero.diagram.layerValue')}</p>
                </div>
                <div className="flex justify-center">
                  <span className="h-10 border-l-2 border-dashed border-blue-400" aria-hidden="true" />
                </div>
                <div className="rounded-2xl border border-white/10 bg-white/10 p-4">
                  <p className="text-sm text-slate-300">{t('hero.diagram.outputLabel')}</p>
                  <p className="mt-2 text-2xl font-semibold">{t('hero.diagram.outputValue')}</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </header>
  )
}
