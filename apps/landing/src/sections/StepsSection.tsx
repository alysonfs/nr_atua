const STEPS = [
  {
    number: 1,
    title: 'Crie sua conta',
    description:
      'Cadastre-se com e-mail e senha. Confirme o e-mail e seu período de avaliação começa automaticamente — sem cartão de crédito.',
  },
  {
    number: 2,
    title: 'Conecte suas fontes',
    description:
      'Na área da sua conta, configure o conector inicial disponível para sua operação.',
  },
  {
    number: 3,
    title: 'Ative o Agente Coletor',
    description:
      'Com a configuração pronta, ative o agente para solicitar a primeira coleta operacional.',
  },
] as const

export function StepsSection() {
  return (
    <section id="como-funciona" aria-labelledby="steps-heading" className="bg-slate-100">
      <div className="mx-auto max-w-6xl px-6 py-20 lg:px-10">
        <h2 id="steps-heading" className="text-center text-3xl font-bold tracking-[-0.02em] text-slate-950 sm:text-4xl">
          Comece em três passos
        </h2>
        <ol className="mt-10 grid gap-8 sm:grid-cols-3">
          {STEPS.map((step) => (
            <li key={step.number} className="flex flex-col items-center rounded-2xl border border-slate-200 bg-white p-6 text-center shadow-[0_4px_20px_rgba(15,23,42,0.05)]">
              <span className="flex h-12 w-12 items-center justify-center rounded-full bg-slate-950 text-xl font-bold text-white">
                {step.number}
              </span>
              <h3 className="mt-4 text-lg font-semibold text-slate-950">{step.title}</h3>
              <p className="mt-2 text-slate-600">{step.description}</p>
            </li>
          ))}
        </ol>
      </div>
    </section>
  )
}
