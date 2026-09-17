import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  useActivateCollector,
  useCollectorActivation,
  useDeactivateCollector,
} from '../../hooks/useCollectorActivation'
import type {
  ActivateCollectorErrorCode,
  DeactivateCollectorErrorCode,
} from '../../../../shared/types/integration'

interface CollectorActivationPanelProps {
  tenantId: string
  integrationId: string
  className?: string
}

/**
 * RF-008 (ADR-020/ADR-024): painel de ativação do Agente Coletor.
 *
 * A ativação depende apenas de um plano de tenant elegível (ADR-024); o
 * status de validação das credenciais é exibido de forma informativa, mas
 * não bloqueia o botão de ativação.
 */
export function CollectorActivationPanel({
  tenantId,
  integrationId,
  className = '',
}: CollectorActivationPanelProps) {
  const { t } = useTranslation()
  const { status, isLoading, isError, refetch } = useCollectorActivation(tenantId, integrationId)
  const { activate, isActivating } = useActivateCollector(tenantId, integrationId)
  const { deactivate, isDeactivating } = useDeactivateCollector(tenantId, integrationId)
  const [activateError, setActivateError] = useState<string | null>(null)
  const [deactivateError, setDeactivateError] = useState<string | null>(null)

  const handleActivate = async () => {
    setActivateError(null)
    const result = await activate()
    if (result.status === 'success') {
      await refetch()
      return
    }

    const errorCode: ActivateCollectorErrorCode = result.errorCode ?? 'unknown_error'
    setActivateError(t(`collector.activateErrors.${errorCode}`))
  }

  const handleDeactivate = async () => {
    setDeactivateError(null)
    const result = await deactivate()
    if (result.status === 'success') {
      await refetch()
      return
    }

    const errorCode: DeactivateCollectorErrorCode = result.errorCode ?? 'unknown_error'
    setDeactivateError(t(`collector.deactivateErrors.${errorCode}`))
  }

  if (isLoading) {
    return (
      <div className={`rounded-md border border-slate-200 bg-white p-5 shadow-sm ${className}`}>
        <p className="text-sm text-slate-500">{t('collector.loadingStatus')}</p>
      </div>
    )
  }

  if (isError || !status) {
    return (
      <div
        className={`rounded-md border-l-4 border-red-500 bg-white p-5 shadow-sm ${className}`}
        role="alert"
      >
        <p className="text-sm text-red-800">{t('collector.statusError')}</p>
      </div>
    )
  }

  const isActive = status.status === 'Active'
  const badgeClass = isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-100 text-slate-700'
  const statusLabel = isActive ? t('collector.active') : t('collector.inactive')

  return (
    <div className={`rounded-md border border-slate-200 bg-white shadow-sm ${className}`}>
      <div className="flex items-center justify-between gap-4 border-b border-slate-200 px-5 py-4">
        <h2 className="text-base font-semibold text-slate-950">{t('collector.title')}</h2>
        <span className={`rounded-full px-3 py-1 text-xs font-semibold ${badgeClass}`}>{statusLabel}</span>
      </div>

      <div className="px-5 py-4">
        {!isActive && status.credentialValidationStatus !== 'Succeeded' && (
          <p className="mb-4 text-sm text-slate-600">{t('collector.validationHint')}</p>
        )}

        {!status.canActivate && (
          <div role="alert" className="mb-4 rounded-md border-l-4 border-amber-500 bg-amber-50 p-3">
            <p className="text-sm text-amber-800">
              {t(`collector.blockReasons.${status.activationBlockReason}`, {
                defaultValue: t('collector.blockReasons.PlanIneligible'),
              })}
            </p>
          </div>
        )}

        {activateError && (
          <div role="alert" className="mb-4 rounded-md border-l-4 border-red-500 bg-red-50 p-3">
            <p className="text-sm text-red-800">{activateError}</p>
          </div>
        )}

        {deactivateError && (
          <div role="alert" className="mb-4 rounded-md border-l-4 border-red-500 bg-red-50 p-3">
            <p className="text-sm text-red-800">{deactivateError}</p>
          </div>
        )}

        {isActive && status.activatedAtUtc && (
          <p className="mb-4 text-sm text-slate-600">
            {t('collector.activatedAt', {
              date: new Date(status.activatedAtUtc).toLocaleString(),
            })}
          </p>
        )}

        <p className="mb-4 text-sm text-slate-600">
          {status.lastSuccessfulCollectionAtUtc
            ? t('collector.lastSuccessfulCollectionAt', {
                date: new Date(status.lastSuccessfulCollectionAtUtc).toLocaleString(),
              })
            : t('collector.lastSuccessfulCollectionNever')}
        </p>

        {!isActive && (
          <div className="flex justify-end border-t border-slate-100 pt-4">
            <button
              type="button"
              onClick={handleActivate}
              disabled={isActivating || !status.canActivate}
              className="rounded-md bg-emerald-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-emerald-700 active:bg-emerald-800 disabled:cursor-not-allowed disabled:bg-slate-300"
            >
              {isActivating ? t('collector.activating') : t('collector.activate')}
            </button>
          </div>
        )}

        {isActive && (
          <div className="flex justify-end border-t border-slate-100 pt-4">
            <button
              type="button"
              onClick={handleDeactivate}
              disabled={isDeactivating}
              className="rounded-md bg-white px-4 py-2 text-sm font-semibold text-red-700 ring-1 ring-inset ring-red-300 transition-colors hover:bg-red-50 active:bg-red-100 disabled:cursor-not-allowed disabled:text-slate-400 disabled:ring-slate-200"
            >
              {isDeactivating ? t('collector.deactivating') : t('collector.deactivate')}
            </button>
          </div>
        )}
      </div>
    </div>
  )
}

export default CollectorActivationPanel
