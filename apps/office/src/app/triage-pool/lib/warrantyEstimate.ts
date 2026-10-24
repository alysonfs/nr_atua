import type { TFunction } from 'i18next'
import type { StatusTone } from '../../dashboard/lib/providerStatus'
import type { TriagemWarrantyEstimate } from '../../../shared/types/triagePool'

/**
 * Tonalidade do selo de garantia (`Badge`): verde quando provavelmente em
 * garantia, vermelho quando provavelmente fora, cinza quando indeterminado
 * ou ausente. Nunca uma certeza jurídica — ver `translateWarrantyEstimateLabel`.
 */
export function warrantyEstimateTone(status: TriagemWarrantyEstimate['status'] | undefined): StatusTone {
  switch (status) {
    case 'ProvavelmenteNaGarantia':
      return 'success'
    case 'ProvavelmenteForaDaGarantia':
      return 'danger'
    default:
      return 'neutral'
  }
}

/**
 * Período padrão usado na estimativa, em texto curto ("1 ano", "2 anos",
 * "6 meses"). Sem `defaultMonths` publicado, cai no texto genérico
 * "prazo padrão".
 */
export function formatWarrantyPeriodLabel(t: TFunction, defaultMonths: number | undefined): string {
  if (!defaultMonths) return t('triagePoolDetail.invoice.warranty.periodUnknown')
  if (defaultMonths === 12) return t('triagePoolDetail.invoice.warranty.oneYear')
  if (defaultMonths % 12 === 0) {
    return t('triagePoolDetail.invoice.warranty.years', { count: defaultMonths / 12 })
  }
  return t('triagePoolDetail.invoice.warranty.months', { count: defaultMonths })
}

/**
 * Texto do selo de garantia (C2/OS): sempre deixa claro que é uma
 * *estimativa* com base no prazo padrão — nunca uma certeza jurídica (regra
 * por produto prevista para uma versão futura). Ex.: "Provavelmente na
 * garantia (estimativa: 1 ano)".
 */
export function translateWarrantyEstimateLabel(
  t: TFunction,
  estimate: TriagemWarrantyEstimate | null | undefined,
): string {
  const period = formatWarrantyPeriodLabel(t, estimate?.defaultMonths)

  switch (estimate?.status) {
    case 'ProvavelmenteNaGarantia':
      return t('triagePoolDetail.invoice.warranty.likelyInWarranty', { period })
    case 'ProvavelmenteForaDaGarantia':
      return t('triagePoolDetail.invoice.warranty.likelyOutOfWarranty', { period })
    default:
      return t('triagePoolDetail.invoice.warranty.indeterminate')
  }
}
