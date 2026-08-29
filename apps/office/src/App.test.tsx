import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import App from './App'

vi.mock('./app/office/hooks/useUserTrial', () => ({
  useUserTrial: () => ({
    trial: {
      trialId: 'trial-test',
      activatedAtUtc: '2026-08-22T00:00:00.000Z',
      expiresAtUtc: '2026-08-29T00:00:00.000Z',
      status: 'active' as const,
      daysRemaining: 7,
    },
    summary: {
      isActive: true,
      isExpired: false,
      daysRemaining: 7,
      hoursRemaining: 168,
      isAboutToExpire: false,
      isUrgent: false,
    },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}))

vi.mock('./app/office/hooks/useTimezone', () => ({
  useTimezone: () => ({
    currentTimezone: 'UTC',
    formatDateLocal: () => '29/08/2026',
    updateTimezone: vi.fn(),
    resetToSuggested: vi.fn(),
    getCurrentTimezone: () => 'UTC',
    isLoading: false,
    error: null,
  }),
}))

describe('App', () => {
  it('renders the Trial and timezone preference sections', () => {
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Seu Trial' })).toBeInTheDocument()
    expect(screen.getByText('Trial Ativo')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Preferências' })).toBeInTheDocument()
    expect(screen.getByText('Fuso Horário')).toBeInTheDocument()
  })
})
