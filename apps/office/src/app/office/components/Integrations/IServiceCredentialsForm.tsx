import { type FormEvent, useState } from 'react'
import { PreviewOpen, PreviewClose } from '@icon-park/react'
import { useIServiceCredentials, useSetIServiceCredentials } from '../../hooks/useIServiceIntegration'
import type { SetCredentialsErrorCode } from '../../../../shared/types/integration'
import { useTranslation } from 'react-i18next'

interface IServiceCredentialsFormProps {
  tenantId: string
  integrationId: string
  onSaved: () => void
  className?: string
}

/** Botão "i" com tooltip do daisyUI (hover/foco, sem JS) explicando o campo. */
function InfoTooltip({ text, ariaLabel }: { text: string; ariaLabel: string }) {
  return (
    <div className="tooltip tooltip-right align-middle">
      <div className="tooltip-content">
        <p className="w-56 text-left text-xs font-normal normal-case">{text}</p>
      </div>
      <button
        type="button"
        aria-label={ariaLabel}
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
  const { t } = useTranslation()
  const { setCredentials, isSubmitting } = useSetIServiceCredentials(tenantId, integrationId)
  const { status } = useIServiceCredentials(tenantId, integrationId)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [baseUrl, setBaseUrl] = useState('')
  const [usernameError, setUsernameError] = useState<string | null>(null)
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [isEditing, setIsEditing] = useState(false)
  const [savedLocally, setSavedLocally] = useState(false)
  const [isPasswordVisible, setIsPasswordVisible] = useState(false)
  const isPlaceholder = Boolean(status?.hasCredentials) && !isEditing && !savedLocally
  const isLocked = savedLocally || (Boolean(status?.hasCredentials) && !isEditing)

  const fieldsDisabled = isSubmitting || isLocked

  const handleEdit = () => {
    setIsEditing(true)
    setSavedLocally(false)
    if (isPlaceholder) {
      // Nunca houve valor real em memória (veio apenas do status do backend): limpa para digitação nova.
      setUsername('')
      setPassword('')
    }
  }

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (isSubmitting || isLocked) return

    setSubmitError(null)

    let hasError = false
    if (!username.trim()) {
      setUsernameError(t('integration.usernameRequired'))
      hasError = true
    } else {
      setUsernameError(null)
    }

    if (!password) {
      setPasswordError(t('integration.passwordRequired'))
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
      setSavedLocally(true)
      setIsEditing(false)
      onSaved()
      return
    }

    const errorCode: SetCredentialsErrorCode = result.errorCode ?? 'unknown_error'
    setSubmitError(t(`integration.saveErrors.${errorCode}`))
  }

  return (
    <div className={`rounded-md border border-slate-200 bg-white shadow-sm ${className}`}>
      <div className="border-b border-slate-200 px-5 py-4">
        <h2 className="text-base font-semibold text-slate-950">{t('integration.credentialsTitle')}</h2>
        <p className="mt-1 max-w-3xl text-sm text-slate-600">
          {t('integration.credentialsDescription')}
        </p>
      </div>

      <form onSubmit={handleSubmit} noValidate className="space-y-4 px-5 py-4">
        <div className="grid gap-4 md:grid-cols-2">
        <div>
          <label
            htmlFor="iservice-username"
            className="mb-1 block text-sm font-medium text-slate-700"
          >
            {t('integration.username')}
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
            {t('integration.password')}
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
                aria-label={isPasswordVisible ? t('integration.hidePassword') : t('integration.showPassword')}
                aria-pressed={isPasswordVisible}
                className="absolute inset-y-0 right-0 flex items-center px-3 text-slate-500 hover:text-slate-700"
              >
                {isPasswordVisible ? (
                  <PreviewClose theme="outline" size={20} aria-hidden="true" />
                ) : (
                  <PreviewOpen theme="outline" size={20} aria-hidden="true" />
                )}
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
            {t('integration.baseUrl')}{' '}
            <span className="font-normal text-slate-500">{t('integration.optional')}</span>
          </label>{' '}
          <InfoTooltip text={t('integration.baseUrlHelp')} ariaLabel={t('integration.fieldHelp')} />
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
              {t('integration.edit')}
            </button>
          ) : (
            <button
              type="submit"
              disabled={isSubmitting}
              className="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-blue-700 active:bg-blue-800 disabled:cursor-not-allowed disabled:bg-slate-300"
            >
              {isSubmitting ? t('integration.saving') : t('integration.save')}
            </button>
          )}
        </div>
      </form>
    </div>
  )
}

export default IServiceCredentialsForm
