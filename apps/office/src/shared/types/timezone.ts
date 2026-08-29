/**
 * RF-004: Timezone preference
 * 
 * Stores the user's timezone preference in IANA format.
 * All dates are stored in UTC and displayed in the user's selected timezone.
 */
export interface TimezoneDTO {
  timezone: string; // IANA format: "America/Sao_Paulo", "Europe/London", etc.
  updatedAt: string; // ISO 8601 format
}

/**
 * Response from GET /api/users/me/timezone
 */
export type GetUserTimezoneResponse = TimezoneDTO;

/**
 * Request body for PUT /api/users/me/timezone
 */
export interface UpdateUserTimezoneRequest {
  timezone: string; // IANA format
}

/**
 * Timezone option for selection
 */
export interface TimezoneOption {
  timezone: string; // IANA format
  label: string; // Display name: "São Paulo (UTC-3)"
  offset: string; // Current UTC offset: "UTC-3", "UTC+0", etc.
  country?: string; // Country code or region
}

/**
 * Timezone suggestion by language
 */
export const TIMEZONE_SUGGESTIONS_BY_LANGUAGE: Record<string, string> = {
  'pt-BR': 'America/Sao_Paulo',
  'pt': 'America/Sao_Paulo',
  'en-US': 'America/New_York',
  'en': 'America/New_York',
  'en-GB': 'Europe/London',
  'es-ES': 'Europe/Madrid',
  'es': 'Europe/Madrid',
  'de-DE': 'Europe/Berlin',
  'de': 'Europe/Berlin',
  'fr-FR': 'Europe/Paris',
  'fr': 'Europe/Paris',
  'it-IT': 'Europe/Rome',
  'it': 'Europe/Rome',
  'ja-JP': 'Asia/Tokyo',
  'ja': 'Asia/Tokyo',
  'zh-CN': 'Asia/Shanghai',
  'zh': 'Asia/Shanghai',
  'ko-KR': 'Asia/Seoul',
  'ko': 'Asia/Seoul',
  'ru-RU': 'Europe/Moscow',
  'ru': 'Europe/Moscow',
  'ar': 'Asia/Dubai',
  'ar-SA': 'Asia/Riyadh',
  'hi': 'Asia/Kolkata',
};

/**
 * Popular timezones for quick selection
 */
export const POPULAR_TIMEZONES = [
  'America/New_York',
  'America/Chicago',
  'America/Los_Angeles',
  'America/Sao_Paulo',
  'America/Buenos_Aires',
  'Europe/London',
  'Europe/Paris',
  'Europe/Berlin',
  'Europe/Moscow',
  'Asia/Dubai',
  'Asia/Shanghai',
  'Asia/Tokyo',
  'Asia/Seoul',
  'Asia/Bangkok',
  'Asia/Singapore',
  'Australia/Sydney',
  'UTC',
];
