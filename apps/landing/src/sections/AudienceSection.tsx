export function AudienceSection() {
  return (
    <section aria-labelledby="audience-heading" className="bg-slate-100">
      <div className="mx-auto grid max-w-6xl gap-10 px-6 py-20 lg:grid-cols-[0.9fr_1.1fr] lg:items-center lg:px-10">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-blue-600">Conectores e provedores</p>
          <h2 id="audience-heading" className="mt-3 text-3xl font-bold tracking-[-0.02em] text-slate-950 sm:text-4xl">
            Comece pelos provedores que sua operação já usa
          </h2>
          <p className="mt-5 text-lg leading-8 text-slate-600">
            O ATUA nasce aberto a múltiplas fontes. iService é o conector inicial,
            mas não é o limite da plataforma.
          </p>
        </div>
        <div className="grid gap-4 sm:grid-cols-3">
          {[
            ['iService', 'Conector inicial'],
            ['Novos provedores', 'Planejado'],
            ['Provedores futuros', 'Base expansível'],
          ].map(([provider, status]) => (
            <article key={provider} className="rounded-2xl border border-slate-200 bg-white p-6 text-center shadow-[0_4px_20px_rgba(15,23,42,0.05)]">
              <span className="rounded-full bg-blue-50 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.12em] text-blue-700">
                Conector
              </span>
              <h3 className="mt-5 text-xl font-semibold text-slate-950">{provider}</h3>
              <p className="mt-2 text-sm text-slate-500">{status}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  )
}
