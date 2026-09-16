import { useTranslation } from 'react-i18next'
import { SUPPORT_EMAIL } from '../constants'
import logoBgLight from '../../../../assets/logo_bg_light.svg'

type FooterProps = {
  signInUrl: string
  signUpUrl: string
}

export function Footer({ signInUrl, signUpUrl }: FooterProps) {
  const { t } = useTranslation()
  const year = new Date().getFullYear()

  return (
    <footer className="bg-slate-950">
      <div className="mx-auto max-w-6xl px-6 py-10 text-center text-sm text-slate-400 lg:px-10">
        <img
          src={logoBgLight}
          alt={t('common.logoAlt')}
          className="mx-auto h-auto w-32"
        />
        <p className="mt-5 font-medium text-slate-200">
          {t('footer.tagline')}
        </p>

        <nav aria-label={t('footer.linksLabel')} className="mt-4 flex flex-wrap justify-center gap-x-6 gap-y-2">
          <a href={signInUrl} className="link-hover">
            {t('common.actions.signIn')}
          </a>
          <a href={signUpUrl} className="link-hover">
            {t('common.actions.createAccount')}
          </a>
          <a href={`mailto:${SUPPORT_EMAIL}`} className="link-hover">
            {SUPPORT_EMAIL}
          </a>
        </nav>

        <p className="mt-6">
          {t('footer.copyright', { year })}
        </p>

        <p className="mt-2 text-xs text-slate-500">
          {t('footer.earlyStageNotice')}
        </p>
      </div>
    </footer>
  )
}
