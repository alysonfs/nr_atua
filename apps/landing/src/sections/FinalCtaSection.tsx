import { useTranslation } from 'react-i18next'

type FinalCtaSectionProps = {
  signInUrl: string
  signUpUrl: string
}

export function FinalCtaSection({
  signInUrl,
  signUpUrl,
}: FinalCtaSectionProps) {
  const { t } = useTranslation()

  return (
    <section aria-labelledby="final-cta-heading" className="mx-auto max-w-6xl px-6 py-20 text-center lg:px-10">
      <div className="rounded-3xl bg-slate-950 px-6 py-16 text-white sm:px-12">
        <h2 id="final-cta-heading" className="text-3xl font-bold tracking-[-0.02em] sm:text-4xl">
          {t('finalCta.title')}
        </h2>
        <p className="mx-auto mt-5 max-w-2xl text-lg leading-8 text-slate-300">
          {t('finalCta.description')}
        </p>
        <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:justify-center">
          <a href={signUpUrl} className="btn btn-primary btn-lg bg-linear-to-br from-blue-500 to-blue-600">
            {t('common.actions.createAccountFree')}
          </a>
          <a href={signInUrl} className="btn btn-outline btn-lg border-white text-white hover:bg-white hover:text-slate-950">
            {t('common.actions.existingAccountSignIn')}
          </a>
        </div>
      </div>
    </section>
  )
}
