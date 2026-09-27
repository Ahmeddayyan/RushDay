import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { anonymousSession, jsonResponse, mockFetch, problemResponse, route } from '@/test/mockFetch'
import { renderApp } from '@/test/renderWithProviders'

describe('LoginPage', () => {
  it('validates the form before anything is sent', async () => {
    const user = userEvent.setup()
    const fetchMock = mockFetch(anonymousSession())
    renderApp({ route: '/login' })

    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Enter your student number or username')).toBeInTheDocument()
    expect(screen.getByText('Enter your password')).toBeInTheDocument()

    const identifier = screen.getByLabelText('Student number or username')
    expect(identifier).toHaveAttribute('aria-invalid', 'true')
    expect(identifier).toHaveAccessibleDescription('Enter your student number or username')

    const loginCalls = fetchMock.mock.calls.filter(([url]) =>
      String(url).includes('/api/auth/login'),
    )
    expect(loginCalls).toHaveLength(0)
  })

  it('shows the ProblemDetails message when the API rejects the sign-in', async () => {
    const user = userEvent.setup()
    mockFetch(
      anonymousSession(),
      route('POST', '/api/auth/login', () =>
        problemResponse(401, 'Invalid credentials', 'The student number or password is incorrect.'),
      ),
    )
    renderApp({ route: '/login' })

    await user.type(screen.getByLabelText('Student number or username'), 'S000001')
    await user.type(screen.getByLabelText('Password'), 'wrong-password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The student number or password is incorrect.')
  })

  it('stays usable when the login endpoint does not exist yet', async () => {
    const user = userEvent.setup()
    mockFetch(
      anonymousSession(),
      route('POST', '/api/auth/login', () =>
        problemResponse(404, 'Not found', 'No API endpoint matches POST /api/auth/login.'),
      ),
    )
    renderApp({ route: '/login' })

    await user.type(screen.getByLabelText('Student number or username'), 'S000001')
    await user.type(screen.getByLabelText('Password'), 'secret')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No API endpoint matches POST /api/auth/login.')
    expect(alert).toHaveTextContent('Sign-in is not available on this server yet.')
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled()
  })

  it('returns to the page that required sign-in once signed in', async () => {
    const user = userEvent.setup()
    mockFetch(
      anonymousSession(),
      route('POST', '/api/auth/login', () =>
        jsonResponse({
          id: '1',
          username: 'S000001',
          displayName: 'Ada Lovelace',
          roles: ['Student'],
        }),
      ),
    )
    const { router } = renderApp({ route: '/admin' })

    // The guard bounced us to /login and remembered where we were heading.
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))

    await user.type(screen.getByLabelText('Student number or username'), 'S000001')
    await user.type(screen.getByLabelText('Password'), 'secret')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/admin'))
    // A student is signed in but not an admin: refused, not redirected again.
    expect(await screen.findByText('You do not have access to this page')).toBeInTheDocument()
  })
})
