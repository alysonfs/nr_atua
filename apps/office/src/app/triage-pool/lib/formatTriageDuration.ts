import type { TFunction } from 'i18next'

/**
 * Duração decorrida da triagem (requisito do PO): `(finishedAtUtc ?? agora)
 * - startedAtUtc`, em formato curto (ex.: "12 min", "1 h 05 min", "2 d 3 h").
 * As unidades (min/h/d) são mantidas universais entre pt/en/es — apenas o
 * texto ao redor ("em andamento", "finalizada em") é traduzido (ver
 * `formatTriageDurationLabel`).
 */
export function formatShortDuration(durationMs: number): string {
  const totalSeconds = Math.max(0, Math.floor(durationMs / 1000))

  if (totalSeconds < 60) return '< 1 min'

  const totalMinutes = Math.floor(totalSeconds / 60)
  if (totalMinutes < 60) return `${totalMinutes} min`

  const totalHours = Math.floor(totalMinutes / 60)
  const remainingMinutes = totalMinutes % 60
  if (totalHours < 24) return `${totalHours} h ${String(remainingMinutes).padStart(2, '0')} min`

  const totalDays = Math.floor(totalHours / 24)
  const remainingHours = totalHours % 24
  return `${totalDays} d ${remainingHours} h`
}

/**
 * Rótulo completo da duração da triagem, pronto para exibição na lista e no
 * detalhe (C1/C2): "—" sem `startedAtUtc`; "<duração> (em andamento)"
 * enquanto `finishedAtUtc` for `null`/ausente; "Finalizada em <duração>"
 * quando concluída.
 */
export function formatTriageDurationLabel(
  t: TFunction,
  startedAtUtc: string | null | undefined,
  finishedAtUtc: string | null | undefined,
  nowMs: number = Date.now(),
): string {
  if (!startedAtUtc) return t('triageDuration.unknown')

  const startedMs = new Date(startedAtUtc).getTime()
  if (Number.isNaN(startedMs)) return t('triageDuration.unknown')

  if (finishedAtUtc) {
    const finishedMs = new Date(finishedAtUtc).getTime()
    if (Number.isNaN(finishedMs)) return t('triageDuration.unknown')
    return t('triageDuration.finished', { duration: formatShortDuration(finishedMs - startedMs) })
  }

  return t('triageDuration.inProgress', { duration: formatShortDuration(nowMs - startedMs) })
}
