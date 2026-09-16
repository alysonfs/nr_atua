import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { toast } from 'react-toastify'
import { useTranslation } from 'react-i18next'
import { ApiError, apiClient } from '../../../shared/lib/apiClient'
import { AuthLanguageSelector } from '../AuthLanguageSelector'
import { createSignUpSchema, type SignUpFormValues } from '../schemas'
import logoBgLight from '../../../../../../assets/logo_bg_light.svg'
import logoBgDark from '../../../../../../assets/logo_bg_dark.svg'

export function SignUpPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<SignUpFormValues>({
    resolver: zodResolver(createSignUpSchema(t)),
  })

  async function onSubmit(values: SignUpFormValues) {
    setError(null)

    try {
      await apiClient.post('/auth/signup', values)
      // 202 Accepted: redireciona para confirmação de e-mail com state de sucesso
      toast.success(t('auth.signUp.success'))
      void navigate('/confirmar-email', {
        state: { email: values.email, fromSignUp: true },
      })
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 409 && err.code === 'email_already_registered') {
          setError(t('auth.signUp.alreadyRegistered'))
        } else if (err.status === 400 && err.code === 'invalid_email') {
          setError(t('auth.signUp.invalidEmail'))
        } else if (err.status === 400 && err.code === 'invalid_password') {
          setError(t('auth.signUp.invalidPassword'))
        } else {
          setError(t('auth.signUp.genericError'))
          toast.error(t('auth.signUp.genericError'))
        }
      } else {
        setError(t('auth.signUp.genericError'))
        toast.error(t('auth.signUp.genericError'))
      }
    }
  }

  return (
    <main className="relative flex min-h-screen w-full bg-[#f8fafc]">
      <AuthLanguageSelector />
      {/* Coluna institucional (oculta em telas pequenas) */}
      <div className="hidden flex-1 flex-col justify-between bg-[#0f172a] p-20 lg:flex">
        <img src={logoBgDark} alt="Atyno" className="h-[70px] w-[240px]" />

        <div className="flex flex-col gap-6">
          <p className="text-4xl leading-tight font-bold text-white">
            {t('auth.signUp.sideTitle')}
          </p>
          <p className="text-[15px] leading-relaxed text-[#64748b]">
            {t('auth.signUp.sideDescription')}
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
            <p className="text-sm font-semibold text-white">{t('auth.institutional.secureTitle')}</p>
            <p className="text-xs text-[#64748b]">
              {t('auth.institutional.secureDescription')}
            </p>
          </div>
        </div>
      </div>

      {/* Coluna do formulário */}
      <div className="flex flex-1 items-center justify-center p-6 lg:p-10">
        <div className="w-full max-w-[480px] rounded-2xl bg-white p-8 shadow-[0px_8px_12px_rgba(0,0,0,0.05)] sm:p-12">
          {/* Logo visível apenas no mobile, quando a coluna institucional some */}
          <img src={logoBgLight} alt="Atyno" className="mb-8 h-10 w-auto lg:hidden" />

          <div className="mb-8">
            <h1 className="text-[32px] leading-tight font-bold text-[#0e1a30]">{t('auth.signUp.title')}</h1>
            <p className="mt-2 text-base text-[#64748b]">{t('auth.signUp.subtitle')}</p>
          </div>

          <form onSubmit={(e) => void handleSubmit(onSubmit)(e)} noValidate>
            <fieldset disabled={isSubmitting} className="space-y-5">
              <div>
                <label htmlFor="email" className="mb-2 block text-sm font-semibold text-[#0e1a30]">
                  {t('common.email')}
                </label>
                <input
                  id="email"
                  type="email"
                  autoComplete="email"
                  {...register('email')}
                  className="input input-bordered w-full border-[#e2e8f0] bg-white text-[#0e1a30] focus:border-[#3b82f6]"
                  placeholder={t('auth.signIn.emailPlaceholder')}
                />
                {errors.email && (
                  <p className="mt-1.5 text-sm text-red-700">{errors.email.message}</p>
                )}
              </div>

              <div>
                <label htmlFor="password" className="mb-2 block text-sm font-semibold text-[#0e1a30]">
                  {t('common.password')}
                </label>
                <input
                  id="password"
                  type="password"
                  autoComplete="new-password"
                  {...register('password')}
                  className="input input-bordered w-full border-[#e2e8f0] bg-white text-[#0e1a30] focus:border-[#3b82f6]"
                  placeholder="••••••••"
                />
                {errors.password && (
                  <p className="mt-1.5 text-sm text-red-700">{errors.password.message}</p>
                )}
              </div>

              <div>
                <label
                  htmlFor="passwordConfirmation"
                  className="mb-2 block text-sm font-semibold text-[#0e1a30]"
                >
                  {t('auth.signUp.confirmPassword')}
                </label>
                <input
                  id="passwordConfirmation"
                  type="password"
                  autoComplete="new-password"
                  {...register('passwordConfirmation')}
                  className="input input-bordered w-full border-[#e2e8f0] bg-white text-[#0e1a30] focus:border-[#3b82f6]"
                  placeholder="••••••••"
                />
                {errors.passwordConfirmation && (
                  <p className="mt-1.5 text-sm text-red-700">
                    {errors.passwordConfirmation.message}
                  </p>
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
                  <><span className="loading loading-spinner loading-sm" />{t('auth.signUp.submitting')}</>
                ) : (
                  t('auth.signUp.submit')
                )}
              </button>
            </fieldset>
          </form>

          <p className="mt-6 text-center text-sm text-[#64748b]">
            {t('auth.signUp.hasAccount')}{' '}
            <Link to="/login" className="font-semibold text-[#3b82f6] hover:underline">
              {t('auth.signUp.signIn')}
            </Link>
          </p>

          <hr className="my-8 border-[#e2e8f0]" />

          <p className="text-center text-xs text-[#64748b]">
            {t('common.footer')}
          </p>
        </div>
      </div>
    </main>
  )
}
