import { OFFICE_SIGNIN_URL, OFFICE_SIGNUP_URL } from '../constants'

export function FinalCtaSection() {
  return (
    <section aria-labelledby="final-cta-heading" className="mx-auto max-w-3xl px-4 py-16 text-center">
      <h2 id="final-cta-heading" className="text-2xl font-bold text-slate-900 sm:text-3xl">
        Pronto para conectar sua operação?
      </h2>
      <p className="mt-4 text-lg text-slate-600">
        Crie sua conta gratuitamente e explore o ATUA durante o período de avaliação.
        Sem compromisso.
      </p>
      <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:justify-center">
        <a href={OFFICE_SIGNUP_URL} className="btn btn-primary btn-lg">
          Criar conta grátis
        </a>
        <a href={OFFICE_SIGNIN_URL} className="btn btn-outline btn-lg">
          Já tenho conta — Entrar
        </a>
      </div>
    </section>
  )
}
