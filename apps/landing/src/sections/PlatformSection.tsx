import { useTranslation } from 'react-i18next'

export function PlatformSection() {
  const { t } = useTranslation()
  const cards = [
    {
      title: t('platform.cards.organizedData.title'),
      description: t('platform.cards.organizedData.description'),
    },
    {
      title: t('platform.cards.lessFriction.title'),
      description: t('platform.cards.lessFriction.description'),
    },
    {
      title: t('platform.cards.multipleProviders.title'),
      description: t('platform.cards.multipleProviders.description'),
    },
    {
      title: t('platform.cards.continuousGrowth.title'),
      description: t('platform.cards.continuousGrowth.description'),
    },
  ]

  return (
    <section aria-labelledby="platform-heading" className="mx-auto max-w-6xl px-6 py-20 lg:px-10">
      <div className="max-w-3xl">
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-blue-600">{t('platform.eyebrow')}</p>
        <h2 id="platform-heading" className="mt-3 text-3xl font-bold tracking-[-0.02em] text-slate-950 sm:text-4xl">
          {t('platform.title')}
        </h2>
        <p className="mt-5 text-lg leading-8 text-slate-600">
          {t('platform.description')}
        </p>
      </div>
      <div className="mt-10 grid gap-5 md:grid-cols-2 lg:grid-cols-4">
        {cards.map((card) => (
          <article key={card.title} className="rounded-2xl border border-slate-100 bg-white p-6 shadow-[0_4px_20px_rgba(15,23,42,0.05)]">
            <div className="h-1 w-12 rounded-full bg-linear-to-r from-blue-500 to-blue-600" />
            <h3 className="mt-5 text-lg font-semibold text-slate-950">{card.title}</h3>
            <p className="mt-3 leading-7 text-slate-600">{card.description}</p>
          </article>
        ))}
      </div>
    </section>
  )
}
