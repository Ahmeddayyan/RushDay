import { screen, waitFor, within } from '@testing-library/react'
import type { RouteObject } from 'react-router'
import { describe, expect, it } from 'vitest'

import { RequireAuth } from '@/app/guards'
import { AppShell } from '@/components/layout/AppShell'
import { makeLecturerMe, makeStudentMe } from '@/test/factories'
import { authMock } from '@/test/handlers/auth'
import { renderRoutes } from '@/test/render'

import { Component as ChangePasswordPage } from './ChangePasswordPage'

const routes: RouteObject[] = [
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppShell />,
        children: [
          { path: '/account/password', element: <ChangePasswordPage /> },
          { path: '/account', element: <h1>Account page</h1> },
          { path: '/lecturer/modules', element: <h1>My modules</h1> },
          { path: '/lecturer', element: <h1>Lecturer home</h1> },
        ],
      },
    ],
  },
  { path: '/login', element: <h1>Sign in page</h1> },
]

const current = () => screen.getByLabelText(/^Current password/)
const next = () => screen.getByLabelText(/^New password/)
const confirm = () => screen.getByLabelText(/^Confirm new password/)
const submit = () => screen.getByRole('button', { name: 'Change password' })

describe('ChangePasswordPage', () => {
  it('ticks the live checklist as the new password is typed', async () => {
    const { events } = renderRoutes(routes, { route: '/account/password', user: makeStudentMe() })
    const checklist = await screen.findByRole('list', { name: 'Your new password' })
    expect(checklist).toHaveAttribute('aria-live', 'polite')
    expect(within(checklist).getByText('At least 12 characters').parentElement).toHaveTextContent(
      ': not yet',
    )

    await events.type(next(), 'maple-harbour-lantern')
    expect(within(checklist).getByText('At least 12 characters').parentElement).toHaveTextContent(
      ': done',
    )
    expect(
      within(checklist).getByText('At least 4 different characters').parentElement,
    ).toHaveTextContent(': done')
    expect(
      within(checklist).getByText("Doesn't contain your username").parentElement,
    ).toHaveTextContent(': done')

    await events.clear(next())
    await events.type(next(), 'my-rushday-password')
    expect(
      within(checklist).getByText('Doesn\'t contain "rushday"').parentElement,
    ).toHaveTextContent(': not yet')
  })

  it('validates in the browser first: length, confirmation, different from current', async () => {
    const { events } = renderRoutes(routes, { route: '/account/password', user: makeStudentMe() })
    await events.type(await screen.findByLabelText(/^Current password/), 'Current-Password-1')
    await events.type(next(), 'short')
    await events.type(confirm(), 'different')
    await events.click(submit())
    expect(await screen.findByText('Use at least 12 characters.')).toBeInTheDocument()
    expect(screen.getByText("The passwords don't match.")).toBeInTheDocument()

    await events.clear(next())
    await events.type(next(), 'Current-Password-1')
    await events.clear(confirm())
    await events.type(confirm(), 'Current-Password-1')
    await events.click(submit())
    expect(
      await screen.findByText('Choose a password different from your current one.'),
    ).toBeInTheDocument()
  })

  it('maps the server errors to the fields', async () => {
    const { events } = renderRoutes(routes, { route: '/account/password', user: makeStudentMe() })
    await events.type(await screen.findByLabelText(/^Current password/), 'Not-My-Password-1')
    await events.type(next(), 'maple-harbour-lantern')
    await events.type(confirm(), 'maple-harbour-lantern')
    await events.click(submit())
    expect(await screen.findByText('Your current password is incorrect.')).toBeInTheDocument()
    expect(current()).toHaveAttribute('aria-invalid', 'true')

    await events.clear(current())
    await events.type(current(), 'Current-Password-1')
    await events.clear(next())
    await events.type(next(), 'my-long-password-1')
    await events.clear(confirm())
    await events.type(confirm(), 'my-long-password-1')
    await events.click(submit())
    expect(
      await screen.findByText(
        'That password appears in lists of breached passwords. Choose another.',
      ),
    ).toBeInTheDocument()
  })

  it('changes the password and returns to the account page', async () => {
    const { events, router } = renderRoutes(routes, {
      route: '/account/password',
      user: makeStudentMe(),
    })
    await events.type(await screen.findByLabelText(/^Current password/), 'Current-Password-1')
    await events.type(next(), 'maple-harbour-lantern')
    await events.type(confirm(), 'maple-harbour-lantern')
    await events.click(submit())
    expect(await screen.findByText('Your password has been changed.')).toBeInTheDocument()
    await waitFor(() => expect(router.state.location.pathname).toBe('/account'))
  })

  it('in required mode hides navigation, keeps Sign out and continues to returnTo', async () => {
    const user = makeLecturerMe({ mustChangePassword: true })
    const { events, router } = renderRoutes(routes, {
      route: '/account/password?required=1&returnTo=%2Flecturer%2Fmodules',
      user,
    })
    expect(
      await screen.findByText(
        'Your administrator set a temporary password. Choose a new one to continue.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Primary' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open navigation' })).not.toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: /account menu/ }))
    expect(await screen.findByRole('menuitem', { name: 'Sign out' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Account' })).not.toBeInTheDocument()
    await events.keyboard('{Escape}')

    await events.type(current(), 'Current-Password-1')
    await events.type(next(), 'maple-harbour-lantern')
    await events.type(confirm(), 'maple-harbour-lantern')
    await events.click(submit())
    await waitFor(() => expect(router.state.location.pathname).toBe('/lecturer/modules'))
    expect(authMock.user?.mustChangePassword).toBe(false)
    expect(await screen.findByRole('navigation', { name: 'Primary' })).toBeInTheDocument()
  })

  it('shows demo users a note instead of the form', async () => {
    renderRoutes(routes, { route: '/account/password', user: makeStudentMe({ isDemo: true }) })
    expect(
      await screen.findByText("Demo accounts can't change their password."),
    ).toBeInTheDocument()
    expect(screen.queryByLabelText(/^Current password/)).not.toBeInTheDocument()
  })
})
