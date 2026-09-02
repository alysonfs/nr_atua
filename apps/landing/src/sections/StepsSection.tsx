const STEPS = [
  {
    number: 1,
    title: 'Crie sua conta',
    description:
      'Cadastre-se com e-mail e senha. Confirme o e-mail e seu período de avaliação começa automaticamente — sem cartão de crédito.',
  },
  {
    number: 2,
    title: 'Conecte ao iService',
    description:
      'No Office, informe suas credenciais do iService. O ATUA valida a conexão antes de prosseguir.',
  },
  {
    number: 3,
    title: 'Ative o Agente Coletor',
    description:
      'Com a conexão validada, ative o agente. Ele inicia a coleta imediatamente e passa a monitorar suas ordens de serviço em intervalos regulares.',
  },
] as const

export function StepsSection() {
  return (
    <section aria-labelledby="steps-heading" className="bg-base-200">
      <div className="mx-auto max-w-5xl px-4 py-16">
        <h2 id="steps-heading" className="text-center text-2xl font-bold text-slate-900 sm:text-3xl">
          Comece em três passos
        </h2>
        <ol className="mt-10 grid gap-8 sm:grid-cols-3">
          {STEPS.map((step) => (
            <li key={step.number} className="flex flex-col items-center text-center">
              <span className="flex h-12 w-12 items-center justify-center rounded-full bg-primary text-xl font-bold text-primary-content">
                {step.number}
              </span>
              <h3 className="mt-4 text-lg font-semibold text-slate-900">{step.title}</h3>
              <p className="mt-2 text-slate-600">{step.description}</p>
            </li>
          ))}
        </ol>
      </div>
    </section>
  )
}
