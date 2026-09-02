import { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { toast } from 'react-toastify'
import { ApiError, apiClient } from '../../../shared/lib/apiClient'
import { confirmEmailSchema, type ConfirmEmailFormValues } from '../schemas'
import logoBgLight from '../../../../../../assets/logo_bg_light.svg'
import logoBgDark from '../../../../../../assets/logo_bg_dark.svg'

interface LocationState {
  email?: string
  fromSignUp?: boolean
}

export function ConfirmEmailPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const locationState = (location.state ?? {}) as LocationState
  const knownEmail = locationState.email ?? ''

  const [error, setError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ConfirmEmailFormValues>({
    resolver: zodResolver(confirmEmailSchema),
    defaultValues: { email: knownEmail, code: '' },
  })

  // O e-mail já vem preenchido quando o usuário chega vindo do cadastro (RN:
  // não pedimos para ele digitar de novo — só exibimos como informação).
  const hasKnownEmail = knownEmail.length > 0

  async function onSubmit(values: ConfirmEmailFormValues) {
    setError(null)

    try {
      await apiClient.post('/auth/confirm-email', values)
      toast.success('E-mail confirmado! Você já pode entrar.')
      void navigate('/login', {
        state: { emailConfirmed: true },
      })
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 400 && err.code === 'invalid_code') {
          setError('Código inválido. Verifique o e-mail e tente novamente.')
        } else if (err.status === 400 && err.code === 'expired_code') {
          setError('Código expirado. Solicite um novo código e tente novamente.')
        } else if (err.status === 409 && err.code === 'email_already_confirmed') {
          setError('Este e-mail já foi confirmado. Você já pode entrar.')
        } else {
          setError('Não foi possível confirmar o e-mail. Tente novamente mais tarde.')
          toast.error('Não foi possível confirmar o e-mail. Tente novamente mais tarde.')
        }
      } else {
        setError('Não foi possível confirmar o e-mail. Tente novamente mais tarde.')
        toast.error('Não foi possível confirmar o e-mail. Tente novamente mais tarde.')
      }
    }
  }

  return (
    <main className="flex min-h-screen w-full bg-[#f8fafc]">
      {/* Coluna institucional (oculta em telas pequenas) */}
      <div className="hidden flex-1 flex-col justify-between bg-[#0f172a] p-20 lg:flex">
        <img src={logoBgDark} alt="ATUA" className="h-[70px] w-[240px]" />

        <div className="flex flex-col gap-6">
          <p className="text-4xl leading-tight font-bold text-white">Só falta um passo</p>
          <p className="text-[15px] leading-relaxed text-[#64748b]">
            Digite o código que enviamos para o seu e-mail e comece a usar o
            ATUA.
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
              Confirme seu e-mail
            </h1>
            <p className="mt-2 text-base text-[#64748b]">
              {hasKnownEmail
                ? 'Digite abaixo o código que enviamos para o seu e-mail.'
                : 'Informe seu e-mail e o código de confirmação recebido.'}
            </p>
          </div>

          {locationState.fromSignUp && (
            <div className="mb-6 rounded-lg border border-blue-200 bg-blue-50 px-4 py-3">
              <p className="text-sm text-blue-800">
                Conta criada! Verifique sua caixa de entrada e insira o código de confirmação abaixo.
              </p>
            </div>
          )}

          <form onSubmit={(e) => void handleSubmit(onSubmit)(e)} noValidate>
            <fieldset disabled={isSubmitting} className="space-y-5">
              {hasKnownEmail ? (
                // O e-mail já é conhecido (veio do cadastro): mostramos como
                // informação, não como campo editável, mas o valor continua
                // fazendo parte do formulário (enviado no POST).
                <div>
                  <span className="mb-2 block text-sm font-semibold text-[#0e1a30]">E-mail</span>
                  <p className="text-base text-[#0e1a30]">{knownEmail}</p>
                  <input type="hidden" {...register('email')} />
                </div>
              ) : (
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
              )}

              <div>
                <label htmlFor="code" className="mb-2 block text-sm font-semibold text-[#0e1a30]">
                  Código de confirmação
                </label>
                <input
                  id="code"
                  type="text"
                  autoComplete="one-time-code"
                  {...register('code')}
                  className="input input-bordered w-full border-[#e2e8f0] bg-white tracking-widest text-[#0e1a30] focus:border-[#3b82f6]"
                  placeholder="000000"
                />
                {errors.code && (
                  <p className="mt-1.5 text-sm text-red-700">{errors.code.message}</p>
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
                  'Confirmar e-mail'
                )}
              </button>
            </fieldset>
          </form>

          <p className="mt-6 text-center text-sm text-[#64748b]">
            Já confirmou?{' '}
            <Link to="/login" className="font-semibold text-[#3b82f6] hover:underline">
              Entrar
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
