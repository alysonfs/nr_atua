import { useState } from 'react'
import { useIServiceCredentials, useValidateIServiceCredentials } from '../../hooks/useIServiceIntegration'
import type { ValidateCredentialsErrorCode } from '../../../../shared/types/integration'
import { useTranslation } from 'react-i18next'

interface IServiceValidationStatusProps {
  tenantId: string
  integrationId: string
  onValidated?: () => void
  className?: string
}

/**
 * RF-007: exibe o status de validação da integração iService e permite
 * solicitar uma nova validação sob demanda ("testar credenciais").
 *
 * Nunca exibe nenhum dado sensível — apenas validationStatus e o instante
 * da última validação (RF-007.3/RS-001). Enquanto não houver validação
 * `Succeeded`, o controle de ativação do Agente Coletor (RF-008, fora de
 * escopo) permanece indisponível; este componente não implementa RF-008,
 * apenas expõe o status que o habilitará no futuro.
 */
export function IServiceValidationStatus({
  tenantId,
  integrationId,
  onValidated,
  className = '',
}: IServiceValidationStatusProps) {
  const { i18n, t } = useTranslation()
  const { status, isLoading, isError, refetch } = useIServiceCredentials(tenantId, integrationId)
  const { validate, isValidating } = useValidateIServiceCredentials(tenantId, integrationId)
  const [validateError, setValidateError] = useState<string | null>(null)
  const [lastResult, setLastResult] = useState<'Succeeded' | 'Failed' | null>(null)

  const handleValidate = async () => {
    setValidateError(null)
    setLastResult(null)

    const result = await validate()
    if (result.status === 'success' && result.validationStatus) {
      setLastResult(result.validationStatus)
      await refetch()
      onValidated?.()
      return
    }

    const errorCode: ValidateCredentialsErrorCode = result.errorCode ?? 'unknown_error'
    setValidateError(t(`integration.errors.${errorCode}`))
  }

  if (isLoading) {
    return (
      <div className={`rounded-md border border-slate-200 bg-white p-5 shadow-sm ${className}`}>
        <p className="text-sm text-slate-500">{t('integration.loadingStatus')}</p>
      </div>
    )
  }

  if (isError) {
    return (
      <div
        className={`rounded-md border-l-4 border-red-500 bg-white p-5 shadow-sm ${className}`}
        role="alert"
      >
        <p className="text-sm text-red-800">
          {t('integration.statusError')}
        </p>
      </div>
    )
  }

  if (!status || !status.hasCredentials) {
    return (
      <div className={`rounded-md border border-slate-200 bg-white p-5 shadow-sm ${className}`}>
        <p className="text-sm text-slate-600">
          {t('integration.noCredentials')}
        </p>
      </div>
    )
  }

  const validationStatus = status.validationStatus
  let badgeClass = 'bg-slate-100 text-slate-700'
  let statusLabel = t('integration.notValidated')

  if (validationStatus === 'Succeeded') {
    badgeClass = 'bg-emerald-100 text-emerald-700'
    statusLabel = t('integration.valid')
  } else if (validationStatus === 'Failed') {
    badgeClass = 'bg-red-100 text-red-700'
    statusLabel = t('integration.failed')
  }

  return (
    <div className={`rounded-md border border-slate-200 bg-white shadow-sm ${className}`}>
      <div className="flex items-center justify-between gap-4 border-b border-slate-200 px-5 py-4">
        <h2 className="text-base font-semibold text-slate-950">{t('integration.validationTitle')}</h2>
        <span className={`rounded-full px-3 py-1 text-xs font-semibold ${badgeClass}`}>
          {statusLabel}
        </span>
      </div>

      <div className="px-5 py-4">

      {status.lastValidatedAtUtc && (
        <p className="mb-4 text-sm text-slate-600">
          {t('integration.lastValidation', {
            date: new Date(status.lastValidatedAtUtc).toLocaleString(i18n.resolvedLanguage ?? 'pt-BR'),
          })}
        </p>
      )}

      {!status.lastValidatedAtUtc && (
        <p className="mb-4 text-sm text-slate-600">
          {t('integration.validationPending')}
        </p>
      )}

      {lastResult === 'Failed' && (
        <div role="alert" className="mb-4 rounded-md border-l-4 border-red-500 bg-red-50 p-3">
          <p className="text-sm text-red-800">
            {t('integration.validationFailed')}
          </p>
        </div>
      )}

      {lastResult === 'Succeeded' && (
        <div className="mb-4 rounded-md border-l-4 border-emerald-500 bg-emerald-50 p-3">
          <p className="text-sm text-emerald-800">{t('integration.validationSucceeded')}</p>
        </div>
      )}

      {validateError && (
        <div role="alert" className="mb-4 rounded-md border-l-4 border-red-500 bg-red-50 p-3">
          <p className="text-sm text-red-800">{validateError}</p>
        </div>
      )}

        <div className="flex justify-end border-t border-slate-100 pt-4">
          <button
            type="button"
            onClick={handleValidate}
            disabled={isValidating}
            className="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-blue-700 active:bg-blue-800 disabled:cursor-not-allowed disabled:bg-slate-300"
          >
            {isValidating ? t('integration.validating') : t('integration.validate')}
          </button>
        </div>
      </div>
    </div>
  )
}

export default IServiceValidationStatus
