import { OFFICE_SIGNIN_URL, OFFICE_SIGNUP_URL, SUPPORT_EMAIL } from '../constants'
import logoBgLight from '../../../../assets/logo_bg_light.svg'

export function Footer() {
  const year = new Date().getFullYear()

  return (
    <footer className="bg-slate-950">
      <div className="mx-auto max-w-6xl px-6 py-10 text-center text-sm text-slate-400 lg:px-10">
        <img src={logoBgLight} alt="ATUA" className="mx-auto h-auto w-32" />
        <p className="mt-5 font-medium text-slate-200">
          ATUA — Plataforma operacional para empresas de serviços técnicos.
        </p>

        <nav aria-label="Links do rodapé" className="mt-4 flex flex-wrap justify-center gap-x-6 gap-y-2">
          <a href={OFFICE_SIGNIN_URL} className="link-hover">
            Entrar
          </a>
          <a href={OFFICE_SIGNUP_URL} className="link-hover">
            Criar conta
          </a>
          <a href={`mailto:${SUPPORT_EMAIL}`} className="link-hover">
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
