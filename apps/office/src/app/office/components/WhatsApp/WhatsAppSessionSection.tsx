import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { QRCodeSVG } from 'qrcode.react'
import { useWhatsAppSession } from '../../hooks/useWhatsAppSession'
import type { WhatsAppSessionState } from '../../../../shared/types/whatsAppSession'

interface WhatsAppSessionSectionProps {
  tenantId: string
  integrationId: string | null
  /** RF (ADR-049): somente OWNER/ADMIN enxergam esta seção. */
  canManage: boolean
}

const STATE_BADGE_CLASSNAME: Record<WhatsAppSessionState, string> = {
  unknown: 'bg-slate-100 text-slate-700',
  qr_pending: 'bg-amber-100 text-amber-800',
  connected: 'bg-emerald-100 text-emerald-800',
  disconnected: 'bg-amber-100 text-amber-800',
  logged_out: 'bg-red-100 text-red-800',
}

function secondsUntil(isoDate: string): number {
  const diffMs = new Date(isoDate).getTime() - Date.now()
  return Math.max(0, Math.round(diffMs / 1000))
}

/**
 * RF (ADR-049): seção de Configurações onde OWNER/ADMIN leem o estado da
 * sessão do WhatsApp e, quando necessário, o QR para reparear o número
 * sempre que a sessão Baileys se perder (ex.: a cada `make down`/`make up`).
 *
 * O QR nunca é logado — apenas renderizado como imagem a partir do texto
 * bruto devolvido pela API, e some da UI assim que expira (~60s).
 */
export function WhatsAppSessionSection({
  tenantId,
  integrationId,
  canManage,
}: WhatsAppSessionSectionProps) {
  const { t, i18n } = useTranslation()
  const { session, isLoading, errorCode } = useWhatsAppSession(
    canManage ? tenantId : null,
    integrationId,
  )
  const [secondsLeft, setSecondsLeft] = useState<number | null>(null)

  const qrExpiresAtUtc = session?.state === 'qr_pending' ? session.qrExpiresAtUtc : null

  useEffect(() => {
    if (!qrExpiresAtUtc) {
      setSecondsLeft(null)
      return
    }
    setSecondsLeft(secondsUntil(qrExpiresAtUtc))
    const intervalId = setInterval(() => {
      setSecondsLeft(secondsUntil(qrExpiresAtUtc))
    }, 1_000)
    return () => clearInterval(intervalId)
  }, [qrExpiresAtUtc])

  if (!canManage) {
    return (
      <p className="rounded-md bg-slate-100 px-3 py-2 text-sm text-slate-600">
        {t('whatsapp.forbidden')}
      </p>
    )
  }

  if (isLoading) {
    return (
      <p className="rounded-md border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
        {t('whatsapp.loading')}
      </p>
    )
  }

  if (errorCode === 'forbidden') {
    return (
      <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
        {t('whatsapp.forbidden')}
      </p>
    )
  }

  if (errorCode === 'integration_id_required') {
    return (
      <p role="alert" className="rounded-md border-l-4 border-amber-500 bg-white p-4 text-sm text-amber-900 shadow-sm">
        {t('whatsapp.errors.integration_id_required')}
      </p>
    )
  }

  if (errorCode === 'integration_not_found') {
    return (
      <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
        {t('whatsapp.errors.integration_not_found')}
      </p>
    )
  }

  if (errorCode) {
    return (
      <p role="alert" className="rounded-md border-l-4 border-red-500 bg-white p-4 text-sm text-red-800 shadow-sm">
        {t('whatsapp.errors.unknown_error')}
      </p>
    )
  }

  const state = session?.state ?? 'unknown'
  const updatedAtUtc = session?.updatedAtUtc ?? null
  const qr = state === 'qr_pending' && secondsLeft && secondsLeft > 0 ? session?.qr ?? null : null

  const statusMessage =
    state === 'connected'
      ? t('whatsapp.states.connected', { phoneNumber: session?.phoneNumber ?? '' })
      : t(`whatsapp.states.${state}`)

  return (
    <div className="rounded-md border border-slate-200 bg-white p-4 shadow-sm sm:p-5">
      <div className="flex flex-wrap items-center gap-3">
        <span
          className={`rounded-full px-2.5 py-1 text-xs font-semibold ${STATE_BADGE_CLASSNAME[state]}`}
        >
          {statusMessage}
        </span>
        {updatedAtUtc && (
          <span className="text-xs text-slate-500">
            {t('whatsapp.updatedAt', {
              date: new Intl.DateTimeFormat(i18n.language, {
                dateStyle: 'medium',
                timeStyle: 'short',
              }).format(new Date(updatedAtUtc)),
            })}
          </span>
        )}
      </div>

      {state === 'qr_pending' && (
        <div className="mt-4">
          {qr ? (
            <div className="flex flex-col items-start gap-2 sm:flex-row sm:items-center sm:gap-6">
              <div className="rounded-md border border-slate-200 bg-white p-3">
                <QRCodeSVG value={qr} size={192} role="img" aria-label={t('whatsapp.qrAlt')} />
              </div>
              <div className="space-y-1">
                <p className="text-sm text-slate-700">{t('whatsapp.states.qr_pending')}</p>
                {secondsLeft !== null && (
                  <p className="text-sm font-semibold text-slate-900">
                    {t('whatsapp.expiresIn', { seconds: secondsLeft })}
                  </p>
                )}
              </div>
            </div>
          ) : (
            <p className="text-sm text-slate-600">{t('whatsapp.qrExpired')}</p>
          )}
        </div>
      )}
    </div>
  )
}
