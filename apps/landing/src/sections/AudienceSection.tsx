import { useTranslation } from 'react-i18next'

export function AudienceSection() {
  const { t } = useTranslation()
  const providers = [
    {
      name: t('audience.providers.iservice.name'),
      status: t('audience.providers.iservice.status'),
    },
    {
      name: t('audience.providers.new.name'),
      status: t('audience.providers.new.status'),
    },
    {
      name: t('audience.providers.future.name'),
      status: t('audience.providers.future.status'),
    },
  ]

  return (
    <section aria-labelledby="audience-heading" className="bg-slate-100">
      <div className="mx-auto grid max-w-6xl gap-10 px-6 py-20 lg:grid-cols-[0.9fr_1.1fr] lg:items-center lg:px-10">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-blue-600">{t('audience.eyebrow')}</p>
          <h2 id="audience-heading" className="mt-3 text-3xl font-bold tracking-[-0.02em] text-slate-950 sm:text-4xl">
            {t('audience.title')}
          </h2>
          <p className="mt-5 text-lg leading-8 text-slate-600">
            {t('audience.description')}
          </p>
        </div>
        <div className="grid gap-4 sm:grid-cols-3">
          {providers.map((provider) => (
            <article key={provider.name} className="rounded-2xl border border-slate-200 bg-white p-6 text-center shadow-[0_4px_20px_rgba(15,23,42,0.05)]">
              <span className="rounded-full bg-blue-50 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.12em] text-blue-700">
                {t('audience.connectorLabel')}
              </span>
              <h3 className="mt-5 text-xl font-semibold text-slate-950">{provider.name}</h3>
              <p className="mt-2 text-sm text-slate-500">{provider.status}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  )
}
