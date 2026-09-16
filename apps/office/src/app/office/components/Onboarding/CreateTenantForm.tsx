import { type FormEvent, useState } from 'react'
import { useCreateTenant } from '../../hooks/useTenants'
import { formatCnpj, isValidCnpj, normalizeCnpj } from '../../lib/cnpj'
import type { CreateTenantErrorCode } from '../../../../shared/types/tenant'
import { useTranslation } from 'react-i18next'

interface CreateTenantFormProps {
  onCreated: (tenantId: string, integrationId: string) => void
  className?: string
}

/**
 * RF-006.1: criação do tenant na primeira configuração de integração.
 *
 * Exibido quando o usuário autenticado não possui nenhum tenant
 * (RF-005.2 caso de borda: usuário sem tenant é conduzido a este fluxo,
 * não recebe erro genérico de autorização).
 */
export function CreateTenantForm({ onCreated, className = '' }: CreateTenantFormProps) {
  const { t } = useTranslation()
  const { createTenant, isSubmitting } = useCreateTenant()
  const [name, setName] = useState('')
  const [cnpj, setCnpj] = useState('')
  const [nameError, setNameError] = useState<string | null>(null)
  const [cnpjError, setCnpjError] = useState<string | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (isSubmitting) return

    setSubmitError(null)

    let hasError = false
    if (!name.trim()) {
      setNameError(t('onboarding.nameRequired'))
      hasError = true
    } else {
      setNameError(null)
    }

    const normalizedCnpj = normalizeCnpj(cnpj)
    if (!isValidCnpj(cnpj)) {
      setCnpjError(t('onboarding.invalidCnpj'))
      hasError = true
    } else {
      setCnpjError(null)
    }

    if (hasError || !normalizedCnpj) return

    const result = await createTenant(name.trim(), normalizedCnpj)
    if (result.status === 'success' && result.tenantId && result.integrationId) {
      onCreated(result.tenantId, result.integrationId)
      return
    }

    const errorCode: CreateTenantErrorCode = result.errorCode ?? 'unknown_error'
    setSubmitError(t(`onboarding.errors.${errorCode}`))
  }

  return (
    <div className={`rounded-lg border border-slate-200 bg-white p-6 shadow-sm ${className}`}>
      <h2 className="mb-1 text-lg font-semibold text-slate-900">{t('onboarding.createTitle')}</h2>
      <p className="mb-4 text-sm text-slate-600">
        {t('onboarding.createDescription')}
      </p>

      <form onSubmit={handleSubmit} noValidate>
        <div className="mb-4">
          <label htmlFor="tenant-name" className="mb-1 block text-sm font-medium text-slate-700">
            {t('onboarding.companyName')}
          </label>
          <input
            id="tenant-name"
            type="text"
            value={name}
            onChange={(event) => setName(event.target.value)}
            className="w-full rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
            aria-invalid={Boolean(nameError)}
            aria-describedby={nameError ? 'tenant-name-error' : undefined}
            disabled={isSubmitting}
          />
          {nameError && (
            <p id="tenant-name-error" role="alert" className="mt-1 text-sm text-red-600">
              {nameError}
            </p>
          )}
        </div>

        <div className="mb-4">
          <label htmlFor="tenant-cnpj" className="mb-1 block text-sm font-medium text-slate-700">
            CNPJ
          </label>
          <input
            id="tenant-cnpj"
            type="text"
            inputMode="numeric"
            value={cnpj}
            onChange={(event) => setCnpj(formatCnpj(event.target.value))}
            placeholder="00.000.000/0000-00"
            className="w-full rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
            aria-invalid={Boolean(cnpjError)}
            aria-describedby={cnpjError ? 'tenant-cnpj-error' : undefined}
            disabled={isSubmitting}
          />
          {cnpjError && (
            <p id="tenant-cnpj-error" role="alert" className="mt-1 text-sm text-red-600">
              {cnpjError}
            </p>
          )}
        </div>

        {submitError && (
          <div role="alert" className="mb-4 rounded-lg border-l-4 border-red-500 bg-red-50 p-3">
            <p className="text-sm text-red-800">{submitError}</p>
          </div>
        )}

        <button
          type="submit"
          disabled={isSubmitting}
          className="w-full rounded-lg bg-blue-600 px-4 py-2 font-semibold text-white transition-colors hover:bg-blue-700 active:bg-blue-800 disabled:cursor-not-allowed disabled:bg-slate-300"
        >
          {isSubmitting ? t('onboarding.creating') : t('onboarding.create')}
        </button>
      </form>
    </div>
  )
}

export default CreateTenantForm
