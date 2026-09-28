import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { createQueryClient } from '@/api/queryClient'

import { AuthProvider } from './AuthProvider'
import { routes } from './router'

function requestUrl(input: RequestInfo | URL): string {
  if (typeof input === 'string') return input
  if (input instanceof URL) return input.href
  return input.url
}

function stubFetch(me: { status: number; body?: unknown } = { status: 401 }) {
  vi.stubGlobal(
    'fetch',
    vi.fn<typeof fetch>((input) => {
      const url = requestUrl(input)
      if (url.includes('/api/auth/me')) {
        if (me.status === 200) {
          return Promise.resolve(
            new Response(JSON.stringify(me.body), {
              status: 200,
              headers: { 'Content-Type': 'application/json' },
            }),
          )
        }
        return Promise.resolve(
          new Response(JSON.stringify({ title: 'Unauthorized', status: 401 }), {
            status: 401,
            headers: { 'Content-Type': 'application/problem+json' },
          }),
        )
      }
      if (url.includes('/api/auth/csrf')) {
        return Promise.resolve(
          new Response(JSON.stringify({ token: 'test-token' }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        )
      }
      return Promise.resolve(new Response(null, { status: 404 }))
    }),
  )
}

function renderRoutes(initialEntry: string) {
  const router = createMemoryRouter(routes, { initialEntries: [initialEntry] })
  render(
    <QueryClientProvider client={createQueryClient()}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  )
  return router
}

function meBody(role: string) {
  return {
    id: '1',
    username: 'S000001',
    displayName: 'Ada Lovelace',
    role,
    csrfToken: 'tok',
    mustChangePassword: false,
    mfaSetupRequired: false,
  }
}

describe('router', () => {
  beforeEach(() => {
    stubFetch()
  })

  it('shows the booting splash while the session check is in flight', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(() => new Promise<Response>(() => {})),
    )
    renderRoutes('/')

    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('redirects an anonymous visitor at "/" to the login page', async () => {
    const router = renderRoutes('/')

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('renders the not-found page for an unknown route', async () => {
    renderRoutes('/does-not-exist')

    expect(await screen.findByText("That page doesn't exist")).toBeInTheDocument()
  })

  it('sends an anonymous visitor to a guarded route through /login with returnTo', async () => {
    const router = renderRoutes('/account')

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(router.state.location.search).toBe('?returnTo=%2Faccount')
  })

  it('renders the forbidden page directly', async () => {
    renderRoutes('/forbidden')

    expect(await screen.findByText("You don't have access to that page")).toBeInTheDocument()
  })

  it('sends an already-signed-in visitor away from /login to their role home', async () => {
    stubFetch({ status: 200, body: meBody('Student') })
    const router = renderRoutes('/login')

    // No student routes exist yet in stage S3, so the redirect target itself 404s; the point here
    // is only that PublicOnly moved the visitor away from /login.
    await waitFor(() => expect(router.state.location.pathname).toBe('/student'))
  })

  it('sends a signed-in visitor at "/" to their role home (RootRedirect authenticated branch)', async () => {
    stubFetch({ status: 200, body: meBody('Lecturer') })
    const router = renderRoutes('/')

    await waitFor(() => expect(router.state.location.pathname).toBe('/lecturer'))
  })

  it('sends a signed-in visitor at "/" to /admin for the Admin role', async () => {
    stubFetch({ status: 200, body: meBody('Admin') })
    const router = renderRoutes('/')

    await waitFor(() => expect(router.state.location.pathname).toBe('/admin'))
  })

  it('lets a signed-in visitor through a RequireAuth route (no role restriction)', async () => {
    stubFetch({ status: 200, body: meBody('Student') })
    renderRoutes('/account')

    expect(await screen.findByRole('heading', { name: 'Account' })).toBeInTheDocument()
  })
})
