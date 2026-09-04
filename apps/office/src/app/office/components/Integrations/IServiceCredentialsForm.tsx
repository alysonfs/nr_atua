import { type FormEvent, useState } from 'react'
import { useSetIServiceCredentials } from '../../hooks/useIServiceIntegration'
import type { SetCredentialsErrorCode } from '../../../../shared/types/integration'

interface IServiceCredentialsFormProps {
  tenantId: string
  integrationId: string
  onSaved: () => void
  className?: string
}

const ERROR_MESSAGES: Record<SetCredentialsErrorCode, string> = {
  integration_not_found: 'Integração não encontrada. Contate o suporte.',
  invalid_credentials: 'Preencha usuário e senha do iService corretamente.',
  forbidden: 'Apenas o proprietário da empresa pode configurar esta integração.',
  unknown_error: 'Não foi possível salvar as credenciais. Tente novamente.',
}

/**
 * RF-006.2/RF-006.4/RF-006.5: cadastro e alteração de credenciais iService.
 *
 * - Apenas OWNER pode enviar (backend valida; aqui apenas tratamos o erro
 *   403 de forma amigável, RF-006.3).
 * - O segredo digitado nunca é mantido em estado após o envio bem-sucedido
 *   nem é reexibido: os campos são limpos e o componente pai passa a exibir
 *   apenas o status de validação (RF-007.3/RS-001).
 */
export function IServiceCredentialsForm({
  tenantId,
  integrationId,
  onSaved,
  className = '',
}: IServiceCredentialsFormProps) {
  const { setCredentials, isSubmitting } = useSetIServiceCredentials(tenantId, integrationId)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [baseUrl, setBaseUrl] = useState('')
  const [usernameError, setUsernameError] = useState<string | null>(null)
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (isSubmitting) return

    setSubmitError(null)

    let hasError = false
    if (!username.trim()) {
      setUsernameError('Informe o usuário do iService.')
      hasError = true
    } else {
      setUsernameError(null)
    }

    if (!password) {
      setPasswordError('Informe a senha do iService.')
      hasError = true
    } else {
      setPasswordError(null)
    }

    if (hasError) return

    const result = await setCredentials({
      username: username.trim(),
      password,
      baseUrl: baseUrl.trim() || undefined,
    })

    if (result.status === 'success') {
      // Nunca mantemos o segredo em estado após o envio (RS-001).
      setUsername('')
      setPassword('')
      setBaseUrl('')
      onSaved()
      return
    }

    setSubmitError(ERROR_MESSAGES[result.errorCode ?? 'unknown_error'])
  }

  return (
    <div className={`rounded-md border border-slate-200 bg-white shadow-sm ${className}`}>
      <div className="border-b border-slate-200 px-5 py-4">
        <h2 className="text-base font-semibold text-slate-950">Credenciais do iService</h2>
        <p className="mt-1 max-w-3xl text-sm text-slate-600">
          Acesso cifrado para o ciclo do coletor. As credenciais não são exibidas após o cadastro.
        </p>
      </div>

      <form onSubmit={handleSubmit} noValidate className="space-y-4 px-5 py-4">
        <div className="grid gap-4 md:grid-cols-2">
        <div>
          <label
            htmlFor="iservice-username"
            className="mb-1 block text-sm font-medium text-slate-700"
          >
            Usuário
          </label>
          <input
            id="iservice-username"
            type="text"
            autoComplete="off"
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            className="w-full rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
            aria-invalid={Boolean(usernameError)}
            aria-describedby={usernameError ? 'iservice-username-error' : undefined}
            disabled={isSubmitting}
          />
          {usernameError && (
            <p id="iservice-username-error" role="alert" className="mt-1 text-sm text-red-600">
              {usernameError}
            </p>
          )}
        </div>

        <div>
          <label
            htmlFor="iservice-password"
            className="mb-1 block text-sm font-medium text-slate-700"
          >
            Senha
          </label>
          <input
            id="iservice-password"
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            className="w-full rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
            aria-invalid={Boolean(passwordError)}
            aria-describedby={passwordError ? 'iservice-password-error' : undefined}
            disabled={isSubmitting}
          />
          {passwordError && (
            <p id="iservice-password-error" role="alert" className="mt-1 text-sm text-red-600">
              {passwordError}
            </p>
          )}
        </div>
        </div>

        <div>
          <label
            htmlFor="iservice-base-url"
            className="mb-1 block text-sm font-medium text-slate-700"
          >
            URL/tenant do iService{' '}
            <span className="font-normal text-slate-500">(opcional)</span>
          </label>
          <input
            id="iservice-base-url"
            type="text"
            value={baseUrl}
            onChange={(event) => setBaseUrl(event.target.value)}
            className="w-full rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
            disabled={isSubmitting}
          />
        </div>

        {submitError && (
          <div role="alert" className="rounded-md border-l-4 border-red-500 bg-red-50 p-3">
            <p className="text-sm text-red-800">{submitError}</p>
          </div>
        )}

        <div className="flex justify-end border-t border-slate-100 pt-4">
          <button
            type="submit"
            disabled={isSubmitting}
            className="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-blue-700 active:bg-blue-800 disabled:cursor-not-allowed disabled:bg-slate-300"
          >
            {isSubmitting ? 'Salvando...' : 'Salvar credenciais'}
          </button>
        </div>
      </form>
    </div>
  )
}

export default IServiceCredentialsForm
