import { screen, waitFor } from '@testing-library/react'
import type { RouteObject } from 'react-router'
import { describe, expect, it } from 'vitest'

import { anonymousSession, jsonResponse, mockFetch, route } from '@/test/mockFetch'
import { renderApp } from '@/test/renderWithProviders'

import { RequireRole } from './RequireRole'
import type { SessionUser } from './types'

const routes: RouteObject[] = [
  { path: '/login', element: <h1>Sign in</h1> },
  {
    path: '/admin',
    element: (
      <RequireRole roles={['Admin']}>
        <h1>Admin area</h1>
      </RequireRole>
    ),
  },
]

function signedInAs(roles: SessionUser['roles']) {
  return route('GET', '/api/auth/me', () =>
    jsonResponse({ id: '7', username: 'staff.member', displayName: 'Staff Member', roles }),
  )
}

describe('RequireRole', () => {
  it('shows a loading state while the session is being checked', () => {
    mockFetch(() => new Promise(() => {}) as unknown as Response)
    renderApp({ route: '/admin', routes })

    expect(screen.getByRole('status')).toHaveTextContent('Checking your session')
  })

  it('redirects anonymous users to /login and remembers where they were going', async () => {
    mockFetch(anonymousSession())
    const { router } = renderApp({ route: '/admin', routes })

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(router.state.location.state).toEqual({ from: '/admin' })
    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('treats a missing /api/auth/me endpoint as signed out instead of crashing', async () => {
    mockFetch(route('GET', '/api/auth/me', () => new Response(null, { status: 404 })))
    const { router } = renderApp({ route: '/admin', routes })

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
  })

  it('refuses a signed-in user without the role', async () => {
    mockFetch(signedInAs(['Staff']))
    const { router } = renderApp({ route: '/admin', routes })

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'You do not have access to this page',
    )
    expect(router.state.location.pathname).toBe('/admin')
  })

  it('renders the page for a user with the role', async () => {
    mockFetch(signedInAs(['Admin']))
    renderApp({ route: '/admin', routes })

    expect(await screen.findByRole('heading', { name: 'Admin area' })).toBeInTheDocument()
  })
})
