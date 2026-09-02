import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { toast } from 'react-toastify'
import { ApiError } from '../../../shared/lib/apiClient'
import { useAuth } from '../AuthContext'
import { signInSchema, type SignInFormValues } from '../schemas'
import logoBgLight from '../../../../../../assets/logo_bg_light.svg'
import logoBgDark from '../../../../../../assets/logo_bg_dark.svg'

export function SignInPage() {
  const { signIn } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<SignInFormValues>({
    resolver: zodResolver(signInSchema),
  })

  async function onSubmit(values: SignInFormValues) {
    setError(null)

    try {
      await signIn(values.email, values.password)
      void navigate('/', { replace: true })
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        // RN-005.2: Não diferenciar e-mail inexistente de senha incorreta.
        // RN-005.3: O backend atual (AuthService.SignInAsync) trata
        // "email não confirmado" com o mesmo 401 genérico — ver nota no
        // relatório final. Exibimos mensagem genérica conforme RN-005.2.
        setError('E-mail ou senha inválidos. Verifique suas credenciais e tente novamente.')
      } else {
        setError('Não foi possível entrar. Tente novamente mais tarde.')
        toast.error('Não foi possível entrar. Tente novamente mais tarde.')
      }
    }
  }

  return (
    <main className="flex min-h-screen w-full bg-[#f8fafc]">
      {/* Coluna institucional (oculta em telas pequenas) */}
      <div className="hidden flex-1 flex-col justify-between bg-[#0f172a] p-20 lg:flex">
        <img src={logoBgDark} alt="ATUA" className="h-[70px] w-[240px]" />

        <div className="flex flex-col gap-6">
          <p className="text-4xl leading-tight font-bold text-white">
            Conecte sua operação em um único lugar
          </p>
          <p className="text-[15px] leading-relaxed text-[#64748b]">
            Organize dados, acompanhe sua operação e prepare sua empresa para
            conectar vários provedores em uma única plataforma.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-white/10">
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth={2}
              className="size-5 text-white"
              aria-hidden="true"
            >
              <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z" />
            </svg>
          </div>
          <div>
            <p className="text-sm font-semibold text-white">Ambiente 100% seguro</p>
            <p className="text-xs text-[#64748b]">
              Criptografia de ponta a ponta na sua infraestrutura técnica.
            </p>
          </div>
        </div>
      </div>

      {/* Coluna do formulário */}
      <div className="flex flex-1 items-center justify-center p-6 lg:p-10">
        <div className="w-full max-w-[480px] rounded-2xl bg-white p-8 shadow-[0px_8px_12px_rgba(0,0,0,0.05)] sm:p-12">
          {/* Logo visível apenas no mobile, quando a coluna institucional some */}
          <img src={logoBgLight} alt="ATUA" className="mb-8 h-10 w-auto lg:hidden" />

          <div className="mb-8">
            <h1 className="text-[32px] leading-tight font-bold text-[#0e1a30]">
              Entrar na sua conta
            </h1>
            <p className="mt-2 text-base text-[#64748b]">Acesse sua operação no ATUA.</p>
          </div>

          <form onSubmit={(e) => void handleSubmit(onSubmit)(e)} noValidate>
            <fieldset disabled={isSubmitting} className="space-y-5">
              <div>
                <label htmlFor="email" className="mb-2 block text-sm font-semibold text-[#0e1a30]">
                  E-mail
                </label>
                <input
                  id="email"
                  type="email"
                  autoComplete="email"
                  {...register('email')}
                  className="input input-bordered w-full border-[#e2e8f0] bg-white text-[#0e1a30] focus:border-[#3b82f6]"
                  placeholder="seuemail@empresa.com"
                />
                {errors.email && (
                  <p className="mt-1.5 text-sm text-red-700">{errors.email.message}</p>
                )}
              </div>

              <div>
                <label htmlFor="password" className="mb-2 block text-sm font-semibold text-[#0e1a30]">
                  Senha
                </label>
                <input
                  id="password"
                  type="password"
                  autoComplete="current-password"
                  {...register('password')}
                  className="input input-bordered w-full border-[#e2e8f0] bg-white text-[#0e1a30] focus:border-[#3b82f6]"
                  placeholder="••••••••"
                />
                {errors.password && (
                  <p className="mt-1.5 text-sm text-red-700">{errors.password.message}</p>
                )}
              </div>

              {error && (
                <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-4 py-3">
                  <p className="text-sm text-red-800">{error}</p>
                </div>
              )}

              <button
                type="submit"
                className="btn w-full border-none bg-gradient-to-r from-[#3b82f6] to-[#2563eb] text-white hover:brightness-110"
                aria-busy={isSubmitting}
              >
                {isSubmitting ? (
                  <span className="loading loading-spinner loading-sm" />
                ) : (
                  'Entrar'
                )}
              </button>
            </fieldset>
          </form>

          <p className="mt-6 text-center text-sm text-[#64748b]">
            Ainda não tem conta?{' '}
            <Link to="/cadastro" className="font-semibold text-[#3b82f6] hover:underline">
              Criar conta
            </Link>
          </p>
          <p className="mt-2 text-center text-sm text-[#64748b]">
            Recebeu o código de confirmação?{' '}
            <Link to="/confirmar-email" className="font-semibold text-[#3b82f6] hover:underline">
              Confirmar e-mail
            </Link>
          </p>

          <hr className="my-8 border-[#e2e8f0]" />

          <p className="text-center text-xs text-[#64748b]">
            ATUA — Plataforma operacional para empresas de serviços técnicos.
          </p>
        </div>
      </div>
    </main>
  )
}
