import { OFFICE_SIGNIN_URL, OFFICE_SIGNUP_URL, SUPPORT_EMAIL } from '../constants'

export function Footer() {
  const year = new Date().getFullYear()

  return (
    <footer className="border-t border-base-300 bg-base-100">
      <div className="mx-auto max-w-5xl px-4 py-10 text-center text-sm text-slate-600">
        <p className="font-medium text-slate-700">
          ATUA — Plataforma operacional para empresas de serviços técnicos.
        </p>

        <nav aria-label="Links do rodapé" className="mt-4 flex flex-wrap justify-center gap-x-6 gap-y-2">
          <a href={OFFICE_SIGNIN_URL} className="link link-hover">
            Entrar
          </a>
          <a href={OFFICE_SIGNUP_URL} className="link link-hover">
            Criar conta
          </a>
          <a href={`mailto:${SUPPORT_EMAIL}`} className="link link-hover">
            {SUPPORT_EMAIL}
          </a>
        </nav>

        <p className="mt-6">
          © {year} Assistência Técnica Unificada Ltda. Todos os direitos reservados.
        </p>

        <p className="mt-2 text-xs text-slate-500">
          O ATUA está em fase inicial. Algumas funcionalidades podem estar em
          desenvolvimento ou sujeitas a alteração.
        </p>
      </div>
    </footer>
  )
}
