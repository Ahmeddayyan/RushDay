import { screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { makeAdminMe, makeStudentMe } from '@/test/factories'
import { renderRoutes } from '@/test/render'

import { router as browserRouter, routes } from './router'

describe('router', () => {
  it('is a browser router over the route table', () => {
    expect(browserRouter.routes.length).toBeGreaterThan(0)
  })

  it('sends an anonymous visitor at "/" to the sign-in page', async () => {
    const { router } = renderRoutes(routes, { route: '/', user: null, boot: true })
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
  })

  it('renders the not-found page for an unknown path, inside the shell', async () => {
    renderRoutes(routes, { route: '/does-not-exist', user: null })
    expect(
      await screen.findByRole('heading', { name: "That page doesn't exist" }),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Skip to main content' })).toBeInTheDocument()
  })

  it('renders the public accessibility statement without signing in', async () => {
    renderRoutes(routes, { route: '/accessibility', user: null })
    expect(
      await screen.findByRole('heading', { name: 'Accessibility statement' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('renders the forbidden page directly', async () => {
    renderRoutes(routes, { route: '/forbidden', user: makeStudentMe() })
    expect(
      await screen.findByRole('heading', { name: "You don't have access to that page" }),
    ).toBeInTheDocument()
  })

  it('sends an anonymous visitor to a guarded route through /login with returnTo', async () => {
    const { router } = renderRoutes(routes, { route: '/account', user: null })
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(router.state.location.search).toBe('?returnTo=%2Faccount')
  })

  it('renders the account page in the signed-in shell with the role navigation', async () => {
    renderRoutes(routes, { route: '/account', user: makeAdminMe() })
    expect(await screen.findByRole('heading', { name: 'Account', level: 1 })).toBeInTheDocument()
    const nav = screen.getAllByRole('navigation', { name: 'Primary' })[0]
    expect(nav).toBeDefined()
    expect(screen.getByRole('link', { name: 'Audit log' })).toHaveAttribute('href', '/admin/audit')
  })

  it('sends a signed-in visitor away from /login to their role home', async () => {
    const { router } = renderRoutes(routes, { route: '/login', user: makeStudentMe() })
    await waitFor(() => expect(router.state.location.pathname).toBe('/student'))
  })
})
