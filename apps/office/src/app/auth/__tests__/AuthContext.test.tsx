import { StrictMode } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider, useAuth } from '../AuthContext'

const mockPost = vi.fn()

vi.mock('../../../shared/lib/apiClient', () => ({
  apiClient: {
    post: (...args: unknown[]) => mockPost(...args),
  },
  setAccessTokenProvider: vi.fn(),
}))

function AuthProbe() {
  const { isAuthenticated, isLoading } = useAuth()

  return (
    <div data-testid="auth-state">
      {isLoading ? 'loading' : isAuthenticated ? 'authenticated' : 'anonymous'}
    </div>
  )
}

describe('AuthProvider', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('deduplica a restauração de sessão em React StrictMode', async () => {
    let resolveRefresh!: (value: { accessToken: string }) => void
    mockPost.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveRefresh = resolve
      }),
    )

    render(
      <StrictMode>
        <AuthProvider>
          <AuthProbe />
        </AuthProvider>
      </StrictMode>,
    )

    await waitFor(() => {
      expect(mockPost).toHaveBeenCalledTimes(1)
    })
    expect(mockPost).toHaveBeenCalledWith('/auth/refresh')

    resolveRefresh({ accessToken: 'access-token' })

    await waitFor(() => {
      expect(screen.getByTestId('auth-state')).toHaveTextContent('authenticated')
    })
  })
})