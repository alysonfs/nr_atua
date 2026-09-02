export function PlatformSection() {
  return (
    <section aria-labelledby="platform-heading" className="mx-auto max-w-6xl px-6 py-20 lg:px-10">
      <div className="max-w-3xl">
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-blue-600">O que muda na prática</p>
        <h2 id="platform-heading" className="mt-3 text-3xl font-bold tracking-[-0.02em] text-slate-950 sm:text-4xl">
          Uma plataforma para toda a operação
        </h2>
        <p className="mt-5 text-lg leading-8 text-slate-600">
          O ATUA é a camada operacional para empresas de serviços técnicos que
          precisam transformar fontes dispersas em dados organizados, leitura clara
          e contexto compartilhado.
        </p>
      </div>
      <div className="mt-10 grid gap-5 md:grid-cols-2 lg:grid-cols-4">
        {[
          ['Dados organizados', 'Transforme leituras operacionais em uma base interna mais clara.'],
          ['Menos atrito', 'Reduza a troca entre sistemas, planilhas e consultas dispersas.'],
          ['Base para vários provedores', 'Comece pelo conector inicial sem limitar a evolução da plataforma.'],
          ['Crescimento contínuo', 'Prepare a operação para novos provedores e automações graduais.'],
        ].map(([title, description]) => (
          <article key={title} className="rounded-2xl border border-slate-100 bg-white p-6 shadow-[0_4px_20px_rgba(15,23,42,0.05)]">
            <div className="h-1 w-12 rounded-full bg-linear-to-r from-blue-500 to-blue-600" />
            <h3 className="mt-5 text-lg font-semibold text-slate-950">{title}</h3>
            <p className="mt-3 leading-7 text-slate-600">{description}</p>
          </article>
        ))}
      </div>
    </section>
  )
}
