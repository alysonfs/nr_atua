import { useTranslation } from 'react-i18next'

export function TrustSection() {
  const { t } = useTranslation()
  const cards = [
    {
      title: t('trust.cards.reading.title'),
      description: t('trust.cards.reading.description'),
    },
    {
      title: t('trust.cards.traceability.title'),
      description: t('trust.cards.traceability.description'),
    },
    {
      title: t('trust.cards.gradualAutomation.title'),
      description: t('trust.cards.gradualAutomation.description'),
    },
  ]

  return (
    <section aria-labelledby="trust-heading" className="mx-auto max-w-6xl px-6 py-20 lg:px-10">
      <div className="rounded-3xl border border-slate-200 bg-white p-8 shadow-[0_4px_20px_rgba(15,23,42,0.05)] sm:p-12">
        <span className="rounded-full bg-slate-100 px-4 py-2 text-xs font-semibold uppercase tracking-[0.14em] text-slate-700">
          {t('trust.eyebrow')}
        </span>
        <h2 id="trust-heading" className="mt-6 text-3xl font-bold tracking-[-0.02em] text-slate-950 sm:text-4xl">
          {t('trust.title')}
        </h2>
        <p className="mt-5 max-w-3xl text-lg leading-8 text-slate-600">
          {t('trust.description')}
        </p>
        <div className="mt-10 grid gap-4 md:grid-cols-3">
          {cards.map((card) => (
            <article key={card.title} className="rounded-2xl bg-slate-50 p-5">
              <h3 className="font-semibold text-slate-950">{card.title}</h3>
              <p className="mt-2 text-sm leading-6 text-slate-600">{card.description}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  )
}
