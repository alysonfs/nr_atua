import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  useRecurrentCollectionInterval,
  useUpdateRecurrentCollectionInterval,
} from '../../hooks/useRecurrentCollectionInterval'
import type { RecurrentCollectionIntervalErrorCode } from '../../../../shared/types/integration'

const MIN_INTERVAL_MINUTES = 5
const MAX_INTERVAL_MINUTES = 1440

interface RecurrentCollectionIntervalControlProps {
  tenantId: string
  integrationId: string | null
  canManage: boolean
  className?: string
}

/**
 * RF-025: controle "Frequência da coleta" (intervalo de coleta recorrente,
 * em minutos). Visível apenas com integração provisionada e restrito a
 * OWNER/ADMIN do tenant (mesma regra de RF-008).
 *
 * GET/PUT /api/tenants/{tenantId}/integrations/{integrationId}/recurrent-collection-interval
 */
export function RecurrentCollectionIntervalControl({
  tenantId,
  integrationId,
  canManage,
  className = '',
}: RecurrentCollectionIntervalControlProps) {
  const { t } = useTranslation()
  const { interval, isLoading, isError, refetch } = useRecurrentCollectionInterval(
    integrationId ? tenantId : null,
    integrationId,
  )
  const { update, isSaving } = useUpdateRecurrentCollectionInterval(
    integrationId ? tenantId : null,
    integrationId,
  )

  // `editedValue` fica `null` até o usuário digitar algo, momento em que
  // passa a refletir o valor lido da API (evita sincronizar estado via efeito).
  const [editedValue, setEditedValue] = useState<string | null>(null)
  const [validationError, setValidationError] = useState<string | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  if (!integrationId || !canManage) {
    return null
  }

  if (isLoading) {
    return (
      <div className={`rounded-md border border-slate-200 bg-white p-5 shadow-sm ${className}`}>
        <p className="text-sm text-slate-500">{t('integration.recurrentCollectionInterval.loadingStatus')}</p>
      </div>
    )
  }

  if (isError || !interval) {
    return (
      <div
        className={`rounded-md border-l-4 border-red-500 bg-white p-5 shadow-sm ${className}`}
        role="alert"
      >
        <p className="text-sm text-red-800">{t('integration.recurrentCollectionInterval.statusError')}</p>
      </div>
    )
  }

  const displayValue = editedValue ?? String(interval.recurrentCollectionIntervalMinutes)

  const handleSave = async () => {
    setValidationError(null)
    setSaveError(null)
    setSaved(false)

    const parsed = Number(displayValue)
    if (!Number.isInteger(parsed) || parsed < MIN_INTERVAL_MINUTES || parsed > MAX_INTERVAL_MINUTES) {
      setValidationError(t('integration.recurrentCollectionInterval.validationError'))
      return
    }

    const result = await update(parsed)
    if (result.status === 'success') {
      setSaved(true)
      setEditedValue(null)
      await refetch()
      return
    }

    const errorCode: RecurrentCollectionIntervalErrorCode = result.errorCode ?? 'unknown_error'
    setSaveError(t(`integration.recurrentCollectionInterval.errors.${errorCode}`))
  }

  return (
    <div className={`rounded-md border border-slate-200 bg-white shadow-sm ${className}`}>
      <div className="border-b border-slate-200 px-5 py-4">
        <h2 className="text-base font-semibold text-slate-950">
          {t('integration.recurrentCollectionInterval.label')}
        </h2>
        <p className="mt-1 text-sm text-slate-600">{t('integration.recurrentCollectionInterval.help')}</p>
      </div>

      <div className="px-5 py-4">
        {validationError && (
          <div role="alert" className="mb-4 rounded-md border-l-4 border-red-500 bg-red-50 p-3">
            <p className="text-sm text-red-800">{validationError}</p>
          </div>
        )}

        {saveError && (
          <div role="alert" className="mb-4 rounded-md border-l-4 border-red-500 bg-red-50 p-3">
            <p className="text-sm text-red-800">{saveError}</p>
          </div>
        )}

        {saved && !validationError && !saveError && (
          <p className="mb-4 text-sm text-emerald-700">{t('integration.recurrentCollectionInterval.saved')}</p>
        )}

        <div className="flex items-end gap-3">
          <label htmlFor="recurrent-collection-interval" className="flex flex-col gap-1 text-sm text-slate-700">
            {t('integration.recurrentCollectionInterval.label')}
          </label>
          <input
            id="recurrent-collection-interval"
            type="number"
            min={MIN_INTERVAL_MINUTES}
            max={MAX_INTERVAL_MINUTES}
            value={displayValue}
            onChange={(event) => setEditedValue(event.target.value)}
            className="w-28 rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-sky-500 focus:outline-none"
          />

          <button
            type="button"
            onClick={handleSave}
            disabled={isSaving}
            className="rounded-md bg-sky-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-sky-700 active:bg-sky-800 disabled:cursor-not-allowed disabled:bg-slate-300"
          >
            {isSaving ? t('integration.recurrentCollectionInterval.saving') : t('integration.recurrentCollectionInterval.save')}
          </button>
        </div>
      </div>
    </div>
  )
}

export default RecurrentCollectionIntervalControl
