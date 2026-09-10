import { type FormEvent, useEffect, useState } from 'react'
import { useIServiceCredentials, useSetIServiceCredentials } from '../../hooks/useIServiceIntegration'
import type { SetCredentialsErrorCode } from '../../../../shared/types/integration'
import iconEye from '../../../../../../../assets/icon/icon-eye.svg'
import iconEyeOff from '../../../../../../../assets/icon/icon-eye-off.svg'

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

/** Botão "i" com tooltip do daisyUI (hover/foco, sem JS) explicando o campo. */
function InfoTooltip({ text }: { text: string }) {
  return (
    <div className="tooltip tooltip-right align-middle">
      <div className="tooltip-content">
        <p className="w-56 text-left text-xs font-normal normal-case">{text}</p>
      </div>
      <button
        type="button"
        aria-label="Ajuda sobre este campo"
        className="inline-flex size-4 items-center justify-center rounded-full border border-slate-400 text-[10px] font-semibold leading-none text-slate-500 hover:border-slate-600 hover:text-slate-700"
      >
        i
      </button>
    </div>
  )
}

const MASKED_PLACEHOLDER = '••••••••'

/**
 * RF-006.2/RF-006.4/RF-006.5: cadastro e alteração de credenciais iService.
 *
 * - Apenas OWNER pode enviar (backend valida; aqui apenas tratamos o erro
 *   403 de forma amigável, RF-006.3).
 * - Após salvar com sucesso, os campos permanecem preenchidos porém
 *   desabilitados (evita a impressão de que o cadastro falhou/sumiu); o
 *   botão vira "Editar credenciais" para reabilitar os campos e corrigir.
 * - Ao carregar a página com uma credencial já configurada, o backend nunca
 *   devolve o segredo (RS-001/ADR-004/ADR-018/ADR-021); os campos exibem um
 *   placeholder mascarado apenas para indicar que já há dado salvo.
 */
export function IServiceCredentialsForm({
  tenantId,
  integrationId,
  onSaved,
  className = '',
}: IServiceCredentialsFormProps) {
  const { setCredentials, isSubmitting } = useSetIServiceCredentials(tenantId, integrationId)
  const { status } = useIServiceCredentials(tenantId, integrationId)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [baseUrl, setBaseUrl] = useState('')
  const [usernameError, setUsernameError] = useState<string | null>(null)
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [isLocked, setIsLocked] = useState(false)
  const [isPasswordVisible, setIsPasswordVisible] = useState(false)
  const [isPlaceholder, setIsPlaceholder] = useState(false)

  useEffect(() => {
    if (status?.hasCredentials) {
      setIsLocked(true)
      setIsPlaceholder(true)
    }
  }, [status?.hasCredentials])

  const fieldsDisabled = isSubmitting || isLocked

  const handleEdit = () => {
    setIsLocked(false)
    if (isPlaceholder) {
      // Nunca houve valor real em memória (veio apenas do status do backend): limpa para digitação nova.
      setUsername('')
      setPassword('')
    }
    setIsPlaceholder(false)
  }

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (isSubmitting || isLocked) return

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
      // Mantém os valores visíveis, porém travados, para o usuário conferir o que foi salvo.
      setIsLocked(true)
      setIsPlaceholder(false)
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
            value={isPlaceholder ? MASKED_PLACEHOLDER : username}
            onChange={(event) => setUsername(event.target.value)}
            className="w-full rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20 disabled:bg-slate-50 disabled:text-slate-500"
            aria-invalid={Boolean(usernameError)}
            aria-describedby={usernameError ? 'iservice-username-error' : undefined}
            disabled={fieldsDisabled}
            readOnly={isPlaceholder}
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
          <div className="relative">
            <input
              id="iservice-password"
              type={isPasswordVisible && !isPlaceholder ? 'text' : 'password'}
              autoComplete="new-password"
              value={isPlaceholder ? MASKED_PLACEHOLDER : password}
              onChange={(event) => setPassword(event.target.value)}
              className="w-full rounded-lg border border-slate-300 bg-white px-4 py-2 pr-10 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20 disabled:bg-slate-50 disabled:text-slate-500"
              aria-invalid={Boolean(passwordError)}
              aria-describedby={passwordError ? 'iservice-password-error' : undefined}
              disabled={fieldsDisabled}
              readOnly={isPlaceholder}
            />
            {!isPlaceholder && (
              <button
                type="button"
                onClick={() => setIsPasswordVisible((visible) => !visible)}
                aria-label={isPasswordVisible ? 'Ocultar senha' : 'Mostrar senha'}
                aria-pressed={isPasswordVisible}
                className="absolute inset-y-0 right-0 flex items-center px-3 text-slate-500 hover:text-slate-700"
              >
                <img
                  src={isPasswordVisible ? iconEyeOff : iconEye}
                  alt=""
                  aria-hidden="true"
                  className="size-5"
                />
              </button>
            )}
          </div>
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
          </label>{' '}
          <InfoTooltip text="Endereço específico do iService do seu tenant (ex.: subdomínio dedicado do seu provedor). Deixe em branco para usar o endereço padrão — só preencha se o iService informou uma URL customizada para a sua empresa." />
          <input
            id="iservice-base-url"
            type="text"
            value={baseUrl}
            onChange={(event) => setBaseUrl(event.target.value)}
            className="w-full rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20 disabled:bg-slate-50 disabled:text-slate-500"
            disabled={fieldsDisabled}
          />
        </div>

        {submitError && (
          <div role="alert" className="rounded-md border-l-4 border-red-500 bg-red-50 p-3">
            <p className="text-sm text-red-800">{submitError}</p>
          </div>
        )}

        <div className="flex justify-end border-t border-slate-100 pt-4">
          {isLocked ? (
            <button
              type="button"
              onClick={handleEdit}
              className="rounded-md bg-slate-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-slate-700 active:bg-slate-800"
            >
              Editar credenciais
            </button>
          ) : (
            <button
              type="submit"
              disabled={isSubmitting}
              className="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-blue-700 active:bg-blue-800 disabled:cursor-not-allowed disabled:bg-slate-300"
            >
              {isSubmitting ? 'Salvando...' : 'Salvar credenciais'}
            </button>
          )}
        </div>
      </form>
    </div>
  )
}

export default IServiceCredentialsForm
