import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useAssumeTriagem, type AssumeTriagemResult } from '../hooks/useAssumeTriagem'

interface AssumeButtonProps {
  tenantId: string | null
  triagemId: string
  /** Disparado após assumir com sucesso, para a página recarregar o detalhe (épico, C3: "após assumir, atualizar o detalhe"). */
  onAssumed: () => void
}

function formatDateTime(isoDate: string, locale: string): string {
  return new Date(isoDate).toLocaleString(locale, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

/**
 * C3 — botão "Assumir" com confirmação. Em caso de concorrência (409), exibe
 * aviso de quem já assumiu e quando, sem permitir nova tentativa (a triagem
 * já está em estado terminal para o pool).
 */
export function AssumeButton({ tenantId, triagemId, onAssumed }: AssumeButtonProps) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'pt-BR'
  const { assume, isSubmitting } = useAssumeTriagem(tenantId)
  const [isConfirming, setIsConfirming] = useState(false)
  const [result, setResult] = useState<AssumeTriagemResult | null>(null)

  async function handleConfirm() {
    const outcome = await assume(triagemId)
    setResult(outcome)
    setIsConfirming(false)
    if (outcome.status === 'success') {
      onAssumed()
    }
  }

  return (
    <div className="space-y-2">
      {!isConfirming && (
        <button
          type="button"
          onClick={() => setIsConfirming(true)}
          disabled={isSubmitting}
          className="rounded-md bg-sky-700 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-sky-800 disabled:opacity-50"
        >
          {t('triagePoolDetail.assume.button')}
        </button>
      )}

      {isConfirming && (
        <div role="dialog" aria-modal="true" className="rounded-md border border-slate-200 bg-white p-4 shadow-sm">
          <p className="mb-3 text-sm text-slate-700">{t('triagePoolDetail.assume.confirmMessage')}</p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => void handleConfirm()}
              disabled={isSubmitting}
              className="rounded-md bg-sky-700 px-3 py-1.5 text-sm font-semibold text-white hover:bg-sky-800 disabled:opacity-50"
            >
              {isSubmitting ? t('triagePoolDetail.assume.confirming') : t('triagePoolDetail.assume.confirm')}
            </button>
            <button
              type="button"
              onClick={() => setIsConfirming(false)}
              disabled={isSubmitting}
              className="rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-700 hover:bg-slate-50"
            >
              {t('triagePoolDetail.assume.cancel')}
            </button>
          </div>
        </div>
      )}

      {result?.status === 'conflict' && (
        <p role="alert" className="rounded-md border-l-4 border-amber-500 bg-white p-3 text-sm text-amber-800 shadow-sm">
          {result.conflict.assumedByUserId
            ? t('triagePoolDetail.assume.alreadyAssumed', {
                user: result.conflict.assumedByUserId,
                date: result.conflict.assumedAtUtc ? formatDateTime(result.conflict.assumedAtUtc, locale) : '—',
              })
            : t('triagePoolDetail.assume.alreadyAssumedUnknown')}
        </p>
      )}

      {result?.status === 'notFound' && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-3 text-sm text-red-800 shadow-sm">
          {t('triagePoolDetail.notFoundMessage')}
        </p>
      )}

      {result?.status === 'error' && (
        <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-3 text-sm text-red-800 shadow-sm">
          {t('triagePoolDetail.assume.error')}
        </p>
      )}
    </div>
  )
}
