import { OFFICE_SIGNIN_URL, OFFICE_SIGNUP_URL } from '../constants'

export function FinalCtaSection() {
  return (
    <section aria-labelledby="final-cta-heading" className="mx-auto max-w-6xl px-6 py-20 text-center lg:px-10">
      <div className="rounded-3xl bg-slate-950 px-6 py-16 text-white sm:px-12">
        <h2 id="final-cta-heading" className="text-3xl font-bold tracking-[-0.02em] sm:text-4xl">
          Pronto para enxergar sua operação com mais clareza?
        </h2>
        <p className="mx-auto mt-5 max-w-2xl text-lg leading-8 text-slate-300">
          Comece pelo conector disponível hoje e prepare a base para ampliar suas
          integrações amanhã. Crie sua conta gratuitamente e explore o ATUA durante
          o período de avaliação.
        </p>
        <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:justify-center">
          <a href={OFFICE_SIGNUP_URL} className="btn btn-primary btn-lg bg-linear-to-br from-blue-500 to-blue-600">
          Criar conta grátis
          </a>
          <a href={OFFICE_SIGNIN_URL} className="btn btn-outline btn-lg border-white text-white hover:bg-white hover:text-slate-950">
            Já tenho conta — Entrar
          </a>
        </div>
      </div>
    </section>
  )
}
