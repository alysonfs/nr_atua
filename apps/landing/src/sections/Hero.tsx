import { OFFICE_SIGNIN_URL, OFFICE_SIGNUP_URL } from '../constants'
import logoBgLight from '../../../../assets/logo_bg_light.svg'

const HERO_BADGES = ['Plataforma única', 'Dados conectados', 'Vários provedores'] as const

export function Hero() {
  return (
    <header className="overflow-hidden bg-slate-50">
      <div className="mx-auto grid min-h-[78vh] w-full max-w-6xl gap-12 px-6 py-16 lg:grid-cols-[1.05fr_0.95fr] lg:items-center lg:px-10">
        <div>
          <img src={logoBgLight} alt="ATUA" className="h-auto w-36 sm:w-44" />
          <p className="mt-10 text-xs font-semibold uppercase tracking-[0.2em] text-blue-600">
            Conectividade técnica unificada
          </p>
          <h1 className="mt-4 max-w-3xl text-4xl font-bold tracking-[-0.03em] text-slate-950 sm:text-5xl lg:text-6xl">
            Sua operação técnica, unificada em uma plataforma para vários provedores.
          </h1>
          <p className="mt-6 max-w-2xl text-lg leading-8 text-slate-600 sm:text-xl">
            O ATUA organiza dados, acompanhamento e visibilidade operacional para
            empresas de serviços técnicos — começando pelo conector iService e
            preparado para evoluir com novos provedores.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            <a href={OFFICE_SIGNUP_URL} className="btn btn-primary btn-lg bg-linear-to-br from-blue-500 to-blue-600">
              Começar grátis
            </a>
            <a href="#como-funciona" className="btn btn-outline btn-lg border-slate-900 text-slate-900">
              Ver como funciona
            </a>
            <a href={OFFICE_SIGNIN_URL} className="btn btn-ghost btn-lg text-slate-600">
              Entrar
            </a>
          </div>
          <ul className="mt-8 flex flex-wrap gap-3" aria-label="Características da plataforma">
            {HERO_BADGES.map((badge) => (
              <li
                key={badge}
                className="rounded-full border border-blue-100 bg-blue-50 px-4 py-2 text-xs font-semibold uppercase tracking-[0.12em] text-blue-700"
              >
                {badge}
              </li>
            ))}
          </ul>
        </div>

        <div className="relative">
          <div className="absolute -right-24 -top-20 h-72 w-72 rounded-full bg-blue-100 blur-3xl" />
          <div className="relative rounded-3xl border border-slate-200 bg-white p-6 shadow-[0_24px_80px_rgba(15,23,42,0.10)]">
            <div className="rounded-2xl bg-slate-950 p-6 text-white">
              <p className="text-sm font-semibold uppercase tracking-[0.16em] text-blue-300">
                Centro operacional
              </p>
              <div className="mt-8 grid gap-4">
                <div className="rounded-2xl border border-white/10 bg-white/10 p-4">
                  <p className="text-sm text-slate-300">Entrada de dados</p>
                  <p className="mt-2 text-2xl font-semibold">Múltiplos provedores</p>
                </div>
                <div className="flex justify-center">
                  <span className="h-10 border-l-2 border-dashed border-blue-400" aria-hidden="true" />
                </div>
                <div className="rounded-2xl border border-blue-400/40 bg-blue-500/15 p-4">
                  <p className="text-sm text-blue-200">Camada ATUA</p>
                  <p className="mt-2 text-2xl font-semibold">Dados unificados</p>
                </div>
                <div className="flex justify-center">
                  <span className="h-10 border-l-2 border-dashed border-blue-400" aria-hidden="true" />
                </div>
                <div className="rounded-2xl border border-white/10 bg-white/10 p-4">
                  <p className="text-sm text-slate-300">Saída para sua equipe</p>
                  <p className="mt-2 text-2xl font-semibold">Visão operacional</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </header>
  )
}
