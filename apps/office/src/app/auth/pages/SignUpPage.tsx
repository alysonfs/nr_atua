import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../../../shared/lib/apiClient'
import { apiClient } from '../../../shared/lib/apiClient'

export function SignUpPage() {
  const navigate = useNavigate()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [passwordConfirmation, setPasswordConfirmation] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)

    if (password !== passwordConfirmation) {
      setError('As senhas não coincidem.')
      return
    }

    setIsSubmitting(true)

    try {
      await apiClient.post('/auth/signup', { email, password, passwordConfirmation })
      // 202 Accepted: redireciona para confirmação de e-mail com state de sucesso
      void navigate('/confirmar-email', {
        state: { email, fromSignUp: true },
      })
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 409 && err.code === 'email_already_registered') {
          setError('Este e-mail já está cadastrado. Tente entrar ou confirme seu e-mail.')
        } else if (err.status === 400 && err.code === 'invalid_email') {
          setError('E-mail inválido. Verifique e tente novamente.')
        } else if (err.status === 400 && err.code === 'invalid_password') {
          setError(
            'Senha inválida. Use no mínimo 8 caracteres com letras maiúsculas, minúsculas e números.',
          )
        } else {
          setError('Não foi possível criar a conta. Tente novamente mais tarde.')
        }
      } else {
        setError('Não foi possível criar a conta. Tente novamente mais tarde.')
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
          <p className="mt-2 text-slate-600">Crie sua conta</p>
        </div>

        <div className="rounded-2xl bg-white p-8 shadow-sm ring-1 ring-slate-200">
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
                <label htmlFor="password" className="mb-1.5 block text-sm font-medium text-slate-700">
                  Senha
                </label>
                <input
                  id="password"
                  type="password"
                  autoComplete="new-password"
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="input input-bordered w-full"
                  placeholder="••••••••"
                />
              </div>

              <div>
                <label
                  htmlFor="passwordConfirmation"
                  className="mb-1.5 block text-sm font-medium text-slate-700"
                >
                  Confirmar senha
                </label>
                <input
                  id="passwordConfirmation"
                  type="password"
                  autoComplete="new-password"
                  required
                  value={passwordConfirmation}
                  onChange={(e) => setPasswordConfirmation(e.target.value)}
                  className="input input-bordered w-full"
                  placeholder="••••••••"
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
                  'Criar conta'
                )}
              </button>
            </fieldset>
          </form>

          <p className="mt-6 text-center text-sm text-slate-600">
            Já tem conta?{' '}
            <Link to="/login" className="font-medium text-primary hover:underline">
              Entre aqui
            </Link>
          </p>
        </div>
      </div>
    </main>
  )
}
