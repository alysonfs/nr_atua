import { useState } from 'react'
import { useIServiceCredentials, useValidateIServiceCredentials } from '../../hooks/useIServiceIntegration'
import type { ValidateCredentialsErrorCode } from '../../../../shared/types/integration'

interface IServiceValidationStatusProps {
  tenantId: string
  integrationId: string
  className?: string
}

const VALIDATE_ERROR_MESSAGES: Record<ValidateCredentialsErrorCode, string> = {
  credentials_not_configured: 'Cadastre as credenciais antes de testar a validação.',
  forbidden: 'Você não tem permissão para validar esta integração.',
  unknown_error: 'Não foi possível validar as credenciais agora. Tente novamente.',
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
  className = '',
}: IServiceValidationStatusProps) {
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
      return
    }

    setValidateError(VALIDATE_ERROR_MESSAGES[result.errorCode ?? 'unknown_error'])
  }

  if (isLoading) {
    return (
      <div className={`rounded-lg border border-slate-200 bg-white p-6 shadow-sm ${className}`}>
        <p className="text-sm text-slate-500">Carregando status da integração...</p>
      </div>
    )
  }

  if (isError) {
    return (
      <div
        className={`rounded-lg border border-red-200 bg-red-50 p-6 shadow-sm ${className}`}
        role="alert"
      >
        <p className="text-sm text-red-800">
          Não foi possível carregar o status da integração. Tente novamente mais tarde.
        </p>
      </div>
    )
  }

  if (!status || !status.hasCredentials) {
    return (
      <div className={`rounded-lg border border-slate-200 bg-white p-6 shadow-sm ${className}`}>
        <p className="text-sm text-slate-600">
          Nenhuma credencial configurada ainda. Cadastre as credenciais do iService para
          habilitar a validação.
        </p>
      </div>
    )
  }

  const validationStatus = status.validationStatus
  let badgeClass = 'bg-slate-100 text-slate-700'
  let statusLabel = 'Não validado'

  if (validationStatus === 'Succeeded') {
    badgeClass = 'bg-emerald-100 text-emerald-700'
    statusLabel = 'Credenciais válidas'
  } else if (validationStatus === 'Failed') {
    badgeClass = 'bg-red-100 text-red-700'
    statusLabel = 'Falha na validação'
  }

  return (
    <div className={`rounded-lg border border-slate-200 bg-white p-6 shadow-sm ${className}`}>
      <div className="mb-4 flex items-center justify-between">
        <h2 className="text-lg font-semibold text-slate-900">Validação da integração</h2>
        <span className={`rounded-full px-3 py-1 text-xs font-semibold ${badgeClass}`}>
          {statusLabel}
        </span>
      </div>

      {status.lastValidatedAtUtc && (
        <p className="mb-4 text-sm text-slate-600">
          Última validação em {new Date(status.lastValidatedAtUtc).toLocaleString('pt-BR')}
        </p>
      )}

      {!status.lastValidatedAtUtc && (
        <p className="mb-4 text-sm text-slate-600">
          Esta integração ainda não foi validada. A ativação do Agente Coletor permanecerá
          indisponível até que a validação seja concluída com sucesso.
        </p>
      )}

      {lastResult === 'Failed' && (
        <div role="alert" className="mb-4 rounded-lg border-l-4 border-red-500 bg-red-50 p-3">
          <p className="text-sm text-red-800">
            A validação falhou. Verifique as credenciais cadastradas e tente novamente.
          </p>
        </div>
      )}

      {lastResult === 'Succeeded' && (
        <div className="mb-4 rounded-lg border-l-4 border-emerald-500 bg-emerald-50 p-3">
          <p className="text-sm text-emerald-800">Credenciais validadas com sucesso.</p>
        </div>
      )}

      {validateError && (
        <div role="alert" className="mb-4 rounded-lg border-l-4 border-red-500 bg-red-50 p-3">
          <p className="text-sm text-red-800">{validateError}</p>
        </div>
      )}

      <button
        type="button"
        onClick={handleValidate}
        disabled={isValidating}
        className="w-full rounded-lg bg-blue-600 px-4 py-2 font-semibold text-white transition-colors hover:bg-blue-700 active:bg-blue-800 disabled:cursor-not-allowed disabled:bg-slate-300"
      >
        {isValidating ? 'Testando validação...' : 'Testar validação'}
      </button>
    </div>
  )
}

export default IServiceValidationStatus
