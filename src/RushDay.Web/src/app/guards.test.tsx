import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it, vi } from 'vitest'

import { createQueryClient } from '@/api/queryClient'

import { AuthProvider } from './AuthProvider'
import { RequireRole } from './guards'

function requestUrl(input: RequestInfo | URL): string {
  if (typeof input === 'string') return input
  if (input instanceof URL) return input.href
  return input.url
}

function stubMe(role: string) {
  vi.stubGlobal(
    'fetch',
    vi.fn<typeof fetch>((input) => {
      const url = requestUrl(input)
      if (url.includes('/api/auth/me')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              id: '1',
              username: 'x',
              displayName: 'X',
              role,
              csrfToken: 'tok',
              mustChangePassword: false,
              mfaSetupRequired: false,
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
        )
      }
      return Promise.resolve(new Response(null, { status: 404 }))
    }),
  )
}

function renderGuarded(initialEntry: string) {
  const router = createMemoryRouter(
    [
      {
        element: <RequireRole roles={['Admin']} />,
        children: [{ path: '/admin', element: <h1>Admin area</h1> }],
      },
      { path: '/forbidden', element: <h1>Forbidden</h1> },
    ],
    { initialEntries: [initialEntry] },
  )
  render(
    <QueryClientProvider client={createQueryClient()}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  )
  return router
}

describe('RequireRole', () => {
  it('shows the booting splash while the session check is in flight', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(() => new Promise<Response>(() => {})),
    )
    renderGuarded('/admin')

    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('sends an anonymous visitor through /login with returnTo', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(() => Promise.resolve(new Response(null, { status: 401 }))),
    )
    const router = renderGuarded('/admin')

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(router.state.location.search).toBe('?returnTo=%2Fadmin')
  })

  it('sends a signed-in user without the role to /forbidden', async () => {
    stubMe('Student')
    const router = renderGuarded('/admin')

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
    expect(await screen.findByRole('heading', { name: 'Forbidden' })).toBeInTheDocument()
  })

  it('renders the page for a user with the matching role', async () => {
    stubMe('Admin')
    renderGuarded('/admin')

    expect(await screen.findByRole('heading', { name: 'Admin area' })).toBeInTheDocument()
  })
})
