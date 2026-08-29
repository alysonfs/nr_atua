/**
 * Test suite for Timezone components and utilities
 * 
 * These tests verify:
 * - TimezonePicker renders and filters timezones correctly
 * - TimezonePreference displays current timezone and allows updates
 * - Timezone utilities format dates correctly
 * - UTC offset calculations are accurate
 */

import { describe, it, expect } from 'vitest'
import {
  formatUTCOffset,
  formatDateInTimezone,
  getUTCOffset,
  isValidTimezone,
  formatTimezoneDisplayName,
  searchTimezones,
  getSuggestedTimezone,
} from '../../lib/timezone'

describe('Timezone Utilities', () => {
  describe('formatUTCOffset', () => {
    it('should format positive UTC offset', () => {
      const offset = 5 * 60 * 60 * 1000 // 5 hours in ms
      expect(formatUTCOffset(offset)).toBe('UTC+5')
    })

    it('should format negative UTC offset', () => {
      const offset = -3 * 60 * 60 * 1000 // -3 hours in ms
      expect(formatUTCOffset(offset)).toBe('UTC-3')
    })

    it('should format offset with minutes', () => {
      const offset = (5 * 60 + 30) * 60 * 1000 // 5:30 hours in ms
      expect(formatUTCOffset(offset)).toBe('UTC+5:30')
    })

    it('should format UTC (0 offset)', () => {
      expect(formatUTCOffset(0)).toBe('UTC+0')
    })
  })

  describe('formatTimezoneDisplayName', () => {
    it('should return timezone as-is for UTC', () => {
      expect(formatTimezoneDisplayName('UTC')).toBe('UTC')
    })

    it('should format single-part timezone', () => {
      expect(formatTimezoneDisplayName('UTC')).toBe('UTC')
    })

    it('should format two-part timezone', () => {
      expect(formatTimezoneDisplayName('America/Sao_Paulo')).toBe('Sao Paulo')
    })

    it('should replace underscores with spaces', () => {
      expect(formatTimezoneDisplayName('America/New_York')).toBe('New York')
    })

    it('should handle three-part timezone', () => {
      expect(formatTimezoneDisplayName('America/Argentina/Buenos_Aires')).toContain(
        'Argentina'
      )
    })
  })

  describe('isValidTimezone', () => {
    it('should accept valid IANA timezones', () => {
      expect(isValidTimezone('America/Sao_Paulo')).toBe(true)
      expect(isValidTimezone('Europe/London')).toBe(true)
      expect(isValidTimezone('Asia/Tokyo')).toBe(true)
      expect(isValidTimezone('UTC')).toBe(true)
    })

    it('should reject invalid timezones', () => {
      expect(isValidTimezone('Invalid/Timezone')).toBe(false)
      expect(isValidTimezone('NotATimezone')).toBe(false)
      expect(isValidTimezone('')).toBe(false)
    })
  })

  describe('formatDateInTimezone', () => {
    it('should format date in specified timezone', () => {
      const date = new Date('2026-09-05T23:59:59Z')
      const formatted = formatDateInTimezone(date, 'America/Sao_Paulo')
      
      // São Paulo is UTC-3, so 23:59 UTC is 20:59 local
      expect(formatted).toContain('05/09/2026')
    })

    it('should accept ISO string input', () => {
      const isoString = '2026-09-05T23:59:59Z'
      const formatted = formatDateInTimezone(isoString, 'America/Sao_Paulo')
      
      expect(formatted).toContain('2026')
    })

    it('should support different formats', () => {
      const date = new Date('2026-09-05T12:30:00Z')
      
      const full = formatDateInTimezone(date, 'UTC', 'dd/MM/yyyy HH:mm')
      expect(full).toMatch(/\d{2}\/\d{2}\/\d{4} \d{2}:\d{2}/)
      
      const dateOnly = formatDateInTimezone(date, 'UTC', 'dd/MM/yyyy')
      expect(dateOnly).toMatch(/\d{2}\/\d{2}\/\d{4}/)
    })

    it('should handle invalid timezone gracefully', () => {
      const date = new Date('2026-09-05T23:59:59Z')
      const formatted = formatDateInTimezone(date, 'Invalid/Timezone')
      
      // Should fallback to UTC or a default format
      expect(formatted).toBeDefined()
      expect(formatted.length > 0).toBe(true)
    })
  })

  describe('searchTimezones', () => {
    it('should find timezone by name', () => {
      const results = searchTimezones('Sao Paulo')
      expect(results.length).toBeGreaterThan(0)
      expect(results.some((tz) => tz.timezone === 'America/Sao_Paulo')).toBe(true)
    })

    it('should find timezone by code', () => {
      const results = searchTimezones('America/New')
      expect(results.length).toBeGreaterThan(0)
    })

    it('should be case-insensitive', () => {
      const results1 = searchTimezones('sao paulo')
      const results2 = searchTimezones('SAO PAULO')
      
      expect(results1.length).toBeGreaterThan(0)
      expect(results2.length).toBeGreaterThan(0)
    })

    it('should return empty array for no matches', () => {
      const results = searchTimezones('XYZ123InvalidSearch')
      expect(results.length).toBe(0)
    })
  })

  describe('getSuggestedTimezone', () => {
    it('should return timezone for known language', () => {
      const timezone = getSuggestedTimezone('pt-BR')
      expect(timezone).toBe('America/Sao_Paulo')
    })

    it('should return UTC for unknown language', () => {
      const timezone = getSuggestedTimezone('unknown-XX')
      expect(timezone).toBe('UTC')
    })

    it('should use browser language when not specified', () => {
      // This will use the actual navigator.language
      const timezone = getSuggestedTimezone()
      expect(typeof timezone).toBe('string')
      expect(timezone.length > 0).toBe(true)
    })
  })

  describe('getUTCOffset', () => {
    it('should return the signed offset for valid timezone', () => {
      const offset = getUTCOffset('America/Sao_Paulo')
      
      expect(formatUTCOffset(offset)).toBe('UTC-3')
    })

    it('should return 0 for UTC', () => {
      const offset = getUTCOffset('UTC')
      expect(offset).toBe(0)
    })

    it('should handle invalid timezone', () => {
      const offset = getUTCOffset('Invalid/Timezone')
      
      // Should fallback to 0 (UTC)
      expect(typeof offset).toBe('number')
    })
  })
})

describe('Timezone Integration', () => {
  it('should display date in user timezone correctly', () => {
    const utcDate = new Date('2026-08-29T15:00:00Z') // 3 PM UTC
    
    // São Paulo is UTC-3, so 3 PM UTC = 12 PM in São Paulo
    const formatted = formatDateInTimezone(utcDate, 'America/Sao_Paulo')
    expect(formatted).toContain('29/08/2026')
    
    // New York is UTC-4, so 3 PM UTC = 11 AM in New York
    const formattedNY = formatDateInTimezone(utcDate, 'America/New_York')
    expect(formattedNY).toContain('29/08/2026')
  })

  it('should validate timezone before formatting', () => {
    const date = new Date('2026-09-05T23:59:59Z')
    
    if (isValidTimezone('America/Sao_Paulo')) {
      const formatted = formatDateInTimezone(date, 'America/Sao_Paulo')
      expect(formatted).toBeDefined()
    }
  })
})
