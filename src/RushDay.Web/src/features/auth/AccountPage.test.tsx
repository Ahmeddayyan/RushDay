import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import type { RouteObject } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { RequireAuth } from '@/app/guards'
import { AppShell } from '@/components/layout/AppShell'
import { makeAdminMe, makeDashboard, makeLecturerMe, makeStudentMe } from '@/test/factories'
import { renderRoutes } from '@/test/render'

import { Component as AccountPage } from './AccountPage'

const routes: RouteObject[] = [
  {
    element: <RequireAuth />,
    children: [
      { element: <AppShell />, children: [{ path: '/account', element: <AccountPage /> }] },
    ],
  },
  { path: '/login', element: <h1>Sign in page</h1> },
]

const dashboard = http.get('/api/me/dashboard', () => HttpResponse.json(makeDashboard()))

afterEach(() => {
  vi.restoreAllMocks()
})

describe('AccountPage', () => {
  it('shows a student their profile with programme and year, and no two-step card', async () => {
    renderRoutes(routes, { route: '/account', user: makeStudentMe(), handlers: [dashboard] })
    expect(await screen.findByText('BSc Computer Science')).toBeInTheDocument()
    expect(screen.getByText('Year 3')).toBeInTheDocument()
    expect(screen.getAllByText('S000001').length).toBeGreaterThan(0)
    expect(screen.queryByText('Two-step verification')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Change password' })).toHaveAttribute(
      'href',
      '/account/password',
    )
    expect(screen.getByText('Signing out ends your sessions on every device.')).toBeInTheDocument()
    expect(screen.getByRole('radiogroup', { name: 'Appearance' })).toBeInTheDocument()
  })

  it('downloads a student data export', async () => {
    const createObjectURL = vi.fn(() => 'blob:export')
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: createObjectURL })
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    const { events } = renderRoutes(routes, {
      route: '/account',
      user: makeStudentMe(),
      handlers: [
        dashboard,
        http.get('/api/me/export.json', () =>
          HttpResponse.json(
            { student: {} },
            { headers: { 'Content-Disposition': 'attachment; filename="rushday-S000001.json"' } },
          ),
        ),
      ],
    })
    await events.click(
      await screen.findByRole('button', { name: 'Download my data (JSON), downloads a file' }),
    )
    expect(await screen.findByText('Your data has been downloaded.')).toBeInTheDocument()
    expect(click).toHaveBeenCalledTimes(1)
    expect(createObjectURL).toHaveBeenCalledTimes(1)
  })

  it('tells an administrator without a second factor that it is required', async () => {
    renderRoutes(routes, { route: '/account', user: makeAdminMe({ mfaEnabled: false }) })
    expect(await screen.findByText(/Required for administrators/)).toBeInTheDocument()
    expect(screen.getByText('Off')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Set up' })).toHaveAttribute('href', '/account/mfa')
    expect(screen.queryByRole('button', { name: /Download my data/ })).not.toBeInTheDocument()
  })

  it('offers lecturers optional two-step verification and shows when it is on', async () => {
    renderRoutes(routes, { route: '/account', user: makeLecturerMe({ mfaEnabled: true }) })
    expect(await screen.findByText(/^Optional\./)).toBeInTheDocument()
    expect(screen.getByText('On')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Set up' })).not.toBeInTheDocument()
    expect(screen.getAllByText('L00001').length).toBeGreaterThanOrEqual(2)
  })

  it('hides the password and MFA links for demo accounts', async () => {
    renderRoutes(routes, {
      route: '/account',
      user: makeAdminMe({ isDemo: true, mfaEnabled: false }),
    })
    expect(
      await screen.findByText("Demo accounts can't change their password."),
    ).toBeInTheDocument()
    expect(
      screen.getByText("Demo accounts can't enable two-step verification."),
    ).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Change password' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Set up' })).not.toBeInTheDocument()
  })

  it('signs out from the page', async () => {
    const { events, router } = renderRoutes(routes, { route: '/account', user: makeLecturerMe() })
    const card = (await screen.findByText('Sign out everywhere')).closest('div.rounded-lg')
    if (!(card instanceof HTMLElement)) throw new Error('card not found')
    await events.click(within(card).getByRole('button', { name: 'Sign out' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
  })
})
