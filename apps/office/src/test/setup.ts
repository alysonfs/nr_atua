import '@testing-library/jest-dom/vitest'
import { beforeEach } from 'vitest'
import i18n, {
  DEFAULT_LOCALE,
  LOCALE_STORAGE_KEY,
  PENDING_LOCALE_STORAGE_KEY,
} from '../i18n'

beforeEach(async () => {
  localStorage.removeItem(LOCALE_STORAGE_KEY)
  localStorage.removeItem(PENDING_LOCALE_STORAGE_KEY)
  document.documentElement.lang = DEFAULT_LOCALE
  await i18n.changeLanguage(DEFAULT_LOCALE)
})
