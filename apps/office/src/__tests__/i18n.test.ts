import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n, {
  getPendingLocale,
  LOCALE_STORAGE_KEY,
  normalizeLocale,
  PENDING_LOCALE_STORAGE_KEY,
} from '../i18n'
import { apiClient } from '../shared/lib/apiClient'
import {
  changePreferredLocale,
  syncAuthenticatedLocale,
} from '../shared/lib/localePreference'
import { translateProviderStatus } from '../app/dashboard/lib/providerStatus'

describe('Office localization', () => {
  beforeEach(async () => {
    vi.restoreAllMocks()
    localStorage.clear()
    await i18n.changeLanguage('pt-BR')
  })

  it('normalizes supported browser language variants', () => {
    expect(normalizeLocale('pt_BR')).toBe('pt-BR')
    expect(normalizeLocale('en-GB')).toBe('en-US')
    expect(normalizeLocale('es-MX')).toBe('es-AR')
    expect(normalizeLocale('fr-FR')).toBeUndefined()
  })

  it('keeps a public locale change pending for the next authenticated session', async () => {
    await changePreferredLocale('es-AR', false)

    expect(i18n.resolvedLanguage).toBe('es-AR')
    expect(localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('es-AR')
    expect(getPendingLocale()).toBe('es-AR')
  })

  it('persists a pending Landing locale in the authenticated profile', async () => {
    localStorage.setItem(PENDING_LOCALE_STORAGE_KEY, 'en-US')
    const put = vi.spyOn(apiClient, 'put').mockResolvedValue(null)

    await syncAuthenticatedLocale()

    expect(put).toHaveBeenCalledWith('/api/users/me/locale', { locale: 'en-US' })
    expect(localStorage.getItem(PENDING_LOCALE_STORAGE_KEY)).toBeNull()
    expect(i18n.resolvedLanguage).toBe('en-US')
  })

  it('loads the profile locale when there is no pending explicit choice', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({ locale: 'es-AR' })

    await syncAuthenticatedLocale()

    expect(i18n.resolvedLanguage).toBe('es-AR')
    expect(localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('es-AR')
  })

  it('translates known provider statuses and preserves unknown values', async () => {
    await i18n.changeLanguage('pt-BR')

    expect(translateProviderStatus('Payment Approved', i18n.t)).toBe('Pagamento Aprovado')
    expect(translateProviderStatus('assigned', i18n.t)).toBe('Designado')
    expect(translateProviderStatus('Provider Custom Status', i18n.t)).toBe('Provider Custom Status')
  })
})
