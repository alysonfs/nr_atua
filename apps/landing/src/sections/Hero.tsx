import { OFFICE_SIGNIN_URL, OFFICE_SIGNUP_URL } from '../constants'

export function Hero() {
  return (
    <header className="hero bg-base-200 min-h-[70vh]">
      <div className="hero-content w-full max-w-3xl flex-col text-center py-16">
        <h1 className="text-4xl font-bold text-slate-900 sm:text-5xl">
          A complexidade fica dentro do ATUA. A simplicidade fica para você.
        </h1>
        <p className="mt-6 text-lg text-slate-600 sm:text-xl">
          ATUA conecta a operação da sua assistência técnica ao iService em um único
          ambiente — com dados organizados, histórico observado e supervisão em
          tempo real.
        </p>
        <div className="mt-8 flex flex-col gap-3 sm:flex-row">
          <a href={OFFICE_SIGNUP_URL} className="btn btn-primary btn-lg">
            Começar grátis
          </a>
          <a href={OFFICE_SIGNIN_URL} className="btn btn-outline btn-lg">
            Entrar
          </a>
        </div>
      </div>
    </header>
  )
}
