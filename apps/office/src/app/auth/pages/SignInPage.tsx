import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../../../shared/lib/apiClient'
import { useAuth } from '../AuthContext'

export function SignInPage() {
  const { signIn } = useAuth()
  const navigate = useNavigate()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      await signIn(email, password)
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
          <p className="mt-2 text-slate-600">Entre na sua conta</p>
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
                  autoComplete="current-password"
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
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
                  'Entrar'
                )}
              </button>
            </fieldset>
          </form>

          <p className="mt-6 text-center text-sm text-slate-600">
            Não tem conta?{' '}
            <Link to="/cadastro" className="font-medium text-primary hover:underline">
              Crie sua conta
            </Link>
          </p>
          <p className="mt-2 text-center text-sm text-slate-600">
            Recebeu o código de confirmação?{' '}
            <Link to="/confirmar-email" className="font-medium text-primary hover:underline">
              Confirmar e-mail
            </Link>
          </p>
        </div>
      </div>
    </main>
  )
}
