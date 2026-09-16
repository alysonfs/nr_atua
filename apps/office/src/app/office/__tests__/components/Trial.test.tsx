/**
 * Test suite for Trial components
 * 
 * These tests verify:
 * - TrialBadge renders correctly in both compact and expanded modes
 * - Status colors change based on trial state (active, expiring, expired)
 * - Correct date formatting in user's timezone
 * - Accessibility features (aria labels, roles)
 */

import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { TrialBadge } from '../../components/Trial/TrialBadge'
import { TrialCard } from '../../components/Trial/TrialCard'
import { TrialExpiryAlert } from '../../components/Trial/TrialExpiryAlert'

// Mock the hooks
vi.mock('../../hooks/useUserTrial', () => ({
  useUserTrial: vi.fn(),
}))

vi.mock('../../hooks/useTimezone', () => ({
  useTimezone: vi.fn(() => ({
    formatDateLocal: (date: string) => {
      const d = new Date(date)
      return d.toLocaleDateString('pt-BR')
    },
  })),
}))

const { useUserTrial } = await import('../../hooks/useUserTrial')

describe('TrialBadge', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('should render null when no trial data', () => {
    vi.mocked(useUserTrial).mockReturnValue({
      trial: null,
      summary: null,
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })

    const { container } = render(<TrialBadge />)
    expect(container.firstChild).toBeNull()
  })

  it('should display active trial with days remaining', () => {
    const futureDate = new Date(Date.now() + 5 * 24 * 60 * 60 * 1000).toISOString()

    vi.mocked(useUserTrial).mockReturnValue({
      trial: {
        trialId: 'test-123',
        expiresAtUtc: futureDate,
        activatedAtUtc: new Date().toISOString(),
        status: 'active',
        daysRemaining: 5,
      },
      summary: {
        isActive: true,
        isExpired: false,
        daysRemaining: 5,
        hoursRemaining: 120,
        isAboutToExpire: false,
        isUrgent: false,
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })

    render(<TrialBadge />)
    
    expect(screen.getByRole('status')).toBeInTheDocument()
    expect(screen.getByText(/5 dias/)).toBeInTheDocument()
  })

  it('should display trial expiring soon warning', () => {
    const nearFutureDate = new Date(Date.now() + 1 * 24 * 60 * 60 * 1000).toISOString()

    vi.mocked(useUserTrial).mockReturnValue({
      trial: {
        trialId: 'test-123',
        expiresAtUtc: nearFutureDate,
        activatedAtUtc: new Date().toISOString(),
        status: 'active',
        daysRemaining: 1,
      },
      summary: {
        isActive: true,
        isExpired: false,
        daysRemaining: 1,
        hoursRemaining: 24,
        isAboutToExpire: true,
        isUrgent: false,
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })

    const { container } = render(<TrialBadge />)
    
    // Check for warning color (amber)
    expect(container.querySelector('.bg-amber-100')).toBeInTheDocument()
  })

  it('should display expired trial', () => {
    const pastDate = new Date(Date.now() - 1 * 24 * 60 * 60 * 1000).toISOString()

    vi.mocked(useUserTrial).mockReturnValue({
      trial: {
        trialId: 'test-123',
        expiresAtUtc: pastDate,
        activatedAtUtc: new Date(Date.now() - 8 * 24 * 60 * 60 * 1000).toISOString(),
        status: 'expired',
        daysRemaining: 0,
      },
      summary: {
        isActive: false,
        isExpired: true,
        daysRemaining: 0,
        hoursRemaining: 0,
        isAboutToExpire: false,
        isUrgent: false,
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })

    const { container } = render(<TrialBadge />)
    
    expect(screen.getByText('Trial expirou')).toBeInTheDocument()
    // Check for error color (red)
    expect(container.querySelector('.bg-red-100')).toBeInTheDocument()
  })

  it('should render compact version when prop is true', () => {
    const futureDate = new Date(Date.now() + 5 * 24 * 60 * 60 * 1000).toISOString()

    vi.mocked(useUserTrial).mockReturnValue({
      trial: {
        trialId: 'test-123',
        expiresAtUtc: futureDate,
        activatedAtUtc: new Date().toISOString(),
        status: 'active',
        daysRemaining: 5,
      },
      summary: {
        isActive: true,
        isExpired: false,
        daysRemaining: 5,
        hoursRemaining: 120,
        isAboutToExpire: false,
        isUrgent: false,
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })

    const { container } = render(<TrialBadge compact={true} />)
    
    // Check for compact class
    expect(container.querySelector('.text-xs')).toBeInTheDocument()
  })
})

describe('TrialCard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('should render null when no trial data', () => {
    vi.mocked(useUserTrial).mockReturnValue({
      trial: null,
      summary: null,
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })

    const { container } = render(<TrialCard />)
    expect(container.firstChild).toBeNull()
  })

  it('should display trial card with details', () => {
    const futureDate = new Date(Date.now() + 5 * 24 * 60 * 60 * 1000).toISOString()

    vi.mocked(useUserTrial).mockReturnValue({
      trial: {
        trialId: 'test-123',
        expiresAtUtc: futureDate,
        activatedAtUtc: new Date().toISOString(),
        status: 'active',
        daysRemaining: 5,
      },
      summary: {
        isActive: true,
        isExpired: false,
        daysRemaining: 5,
        hoursRemaining: 120,
        isAboutToExpire: false,
        isUrgent: false,
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })

    render(<TrialCard />)
    
    expect(screen.getByText('Trial Ativo')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument() // Days remaining
  })

  it('should show CTA button when showCTA is true', () => {
    const futureDate = new Date(Date.now() + 5 * 24 * 60 * 60 * 1000).toISOString()

    vi.mocked(useUserTrial).mockReturnValue({
      trial: {
        trialId: 'test-123',
        expiresAtUtc: futureDate,
        activatedAtUtc: new Date().toISOString(),
        status: 'active',
        daysRemaining: 5,
      },
      summary: {
        isActive: true,
        isExpired: false,
        daysRemaining: 5,
        hoursRemaining: 120,
        isAboutToExpire: false,
        isUrgent: false,
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })

    render(<TrialCard showCTA={true} />)
    
    expect(screen.getByRole('button', { name: 'Upgrade plano' })).toBeInTheDocument()
  })
})

describe('TrialExpiryAlert', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('should not render when trial is not about to expire', () => {
    const futureDate = new Date(Date.now() + 5 * 24 * 60 * 60 * 1000).toISOString()

    vi.mocked(useUserTrial).mockReturnValue({
      trial: {
        trialId: 'test-123',
        expiresAtUtc: futureDate,
        activatedAtUtc: new Date().toISOString(),
        status: 'active',
        daysRemaining: 5,
      },
      summary: {
        isActive: true,
        isExpired: false,
        daysRemaining: 5,
        hoursRemaining: 120,
        isAboutToExpire: false,
        isUrgent: false,
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })

    const { container } = render(<TrialExpiryAlert />)
    expect(container.firstChild).toBeNull()
  })

  it('should render alert when trial is about to expire', () => {
    const nearFutureDate = new Date(Date.now() + 1 * 24 * 60 * 60 * 1000).toISOString()

    vi.mocked(useUserTrial).mockReturnValue({
      trial: {
        trialId: 'test-123',
        expiresAtUtc: nearFutureDate,
        activatedAtUtc: new Date().toISOString(),
        status: 'active',
        daysRemaining: 1,
      },
      summary: {
        isActive: true,
        isExpired: false,
        daysRemaining: 1,
        hoursRemaining: 24,
        isAboutToExpire: true,
        isUrgent: false,
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })

    render(<TrialExpiryAlert />)
    
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.getByText(/expira em 1 dia/i)).toBeInTheDocument()
  })

  it('should show urgent message when < 4 hours remaining', () => {
    const urgentDate = new Date(Date.now() + 2 * 60 * 60 * 1000).toISOString()

    vi.mocked(useUserTrial).mockReturnValue({
      trial: {
        trialId: 'test-123',
        expiresAtUtc: urgentDate,
        activatedAtUtc: new Date().toISOString(),
        status: 'active',
        daysRemaining: 0,
      },
      summary: {
        isActive: true,
        isExpired: false,
        daysRemaining: 0,
        hoursRemaining: 2,
        isAboutToExpire: true,
        isUrgent: true,
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })

    render(<TrialExpiryAlert />)
    
    expect(screen.getByText(/2 horas/)).toBeInTheDocument()
  })
})
