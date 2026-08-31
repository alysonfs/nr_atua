import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { ApiError, apiClient } from '../../../shared/lib/apiClient'

interface LocationState {
  email?: string
  fromSignUp?: boolean
}

export function ConfirmEmailPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const locationState = (location.state ?? {}) as LocationState

  const [email, setEmail] = useState(locationState.email ?? '')
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      await apiClient.post('/auth/confirm-email', { email, code })
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
        }
      } else {
        setError('Não foi possível confirmar o e-mail. Tente novamente mais tarde.')
      }
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-slate-50 p-4">
      <div className="w-full max-w-md">
        <div className="mb-8 text-center">
          <h1 className="text-3xl font-bold text-slate-900">ATUA Office</h1>
          <p className="mt-2 text-slate-600">Confirme seu e-mail</p>
        </div>

        <div className="rounded-2xl bg-white p-8 shadow-sm ring-1 ring-slate-200">
          {locationState.fromSignUp && (
            <div className="mb-5 rounded-lg border border-blue-200 bg-blue-50 px-4 py-3">
              <p className="text-sm text-blue-800">
                Conta criada! Verifique sua caixa de entrada e insira o código de confirmação abaixo.
              </p>
            </div>
          )}

          <form onSubmit={(e) => void handleSubmit(e)} noValidate>
            <fieldset disabled={isSubmitting} className="space-y-5">
              <div>
                <label htmlFor="email" className="mb-1.5 block text-sm font-medium text-slate-700">
                  E-mail
                </label>
                <input
                  id="email"
                  type="email"
                  autoComplete="email"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="input input-bordered w-full"
                  placeholder="seu@email.com"
                />
              </div>

              <div>
                <label htmlFor="code" className="mb-1.5 block text-sm font-medium text-slate-700">
                  Código de confirmação
                </label>
                <input
                  id="code"
                  type="text"
                  autoComplete="one-time-code"
                  required
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  className="input input-bordered w-full tracking-widest"
                  placeholder="000000"
                />
              </div>

              {error && (
                <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-4 py-3">
                  <p className="text-sm text-red-800">{error}</p>
                </div>
              )}

              <button
                type="submit"
                className="btn btn-primary w-full"
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

          <p className="mt-6 text-center text-sm text-slate-600">
            Já confirmou?{' '}
            <Link to="/login" className="font-medium text-primary hover:underline">
              Entrar
            </Link>
          </p>
        </div>
      </div>
    </main>
  )
}
