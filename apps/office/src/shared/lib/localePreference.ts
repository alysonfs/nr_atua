import i18n, {
  clearPendingLocale,
  getPendingLocale,
  markLocalePending,
  normalizeLocale,
  persistLocale,
  type SupportedLocale,
} from '../../i18n'
import { apiClient } from './apiClient'

interface UserLocaleResponse {
  locale: string
}

async function applyLocale(locale: SupportedLocale): Promise<void> {
  persistLocale(locale)
  document.documentElement.lang = locale
  await i18n.changeLanguage(locale)
}

export async function syncAuthenticatedLocale(): Promise<void> {
  const pendingLocale = getPendingLocale()
  if (pendingLocale) {
    await apiClient.put('/api/users/me/locale', { locale: pendingLocale })
    await applyLocale(pendingLocale)
    clearPendingLocale()
    return
  }

  const response = await apiClient.get<UserLocaleResponse>('/api/users/me/locale')
  const profileLocale = normalizeLocale(response?.locale)
  if (profileLocale) await applyLocale(profileLocale)
}

export async function changePreferredLocale(
  locale: SupportedLocale,
  authenticated: boolean,
): Promise<void> {
  await applyLocale(locale)

  if (!authenticated) {
    markLocalePending(locale)
    return
  }

  try {
    await apiClient.put('/api/users/me/locale', { locale })
    clearPendingLocale()
  } catch (error) {
    markLocalePending(locale)
    throw error
  }
}
