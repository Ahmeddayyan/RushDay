import { screen, waitFor, within } from '@testing-library/react'
import { http } from 'msw'
import type { RouteObject } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { PublicOnly } from '@/app/guards'
import { makeDemoStatus, makePublicationBrief, makePublicStatus } from '@/test/factories'
import { TEST_TOTP_CODE } from '@/test/handlers/auth'
import { statusFailureHandler, statusHandler } from '@/test/handlers/public'
import { problem } from '@/test/http'
import { renderRoutes } from '@/test/render'

import { Component as LoginPage } from './LoginPage'

const STORY =
  "I built RushDay because my own university's portal fell over every results day and enrolment window. I wanted to understand why that happens and to build a portal that doesn't crash under the same load."

const routes: RouteObject[] = [
  { element: <PublicOnly />, children: [{ path: '/login', element: <LoginPage /> }] },
  { path: '/student', element: <h1>Student home</h1> },
  { path: '/admin', element: <h1>Admin overview</h1> },
  { path: '/account', element: <h1>Account page</h1> },
]

function renderLogin(route = '/login', handlers = [statusHandler(makeDemoStatus())]) {
  return renderRoutes(routes, { route, user: null, handlers })
}

const usernameField = () => screen.getByLabelText('Student number, staff number or admin username')
const passwordField = () => screen.getByLabelText('Password')
const signIn = () => screen.getByRole('button', { name: 'Sign in' })

afterEach(() => {
  vi.useRealTimers()
})

describe('LoginPage', () => {
  it('validates both fields before sending anything', async () => {
    const { events } = renderLogin()
    await events.click(signIn())

    expect(
      await screen.findByText('Enter your student number, staff number or admin username.'),
    ).toBeInTheDocument()
    expect(screen.getByText('Enter your password.')).toBeInTheDocument()
    expect(usernameField()).toHaveAttribute('aria-invalid', 'true')
    // Only the first error is announced as an alert.
    expect(screen.getAllByRole('alert')).toHaveLength(1)
  })

  it('offers the demo accounts, and "Use" fills the form and focuses Sign in', async () => {
    const { events } = renderLogin()
    const panel = await screen.findByRole('region', { name: 'Try the demo' })
    expect(
      within(panel).getByText('Demo data: 20,000 synthetic students, no real people.'),
    ).toBeInTheDocument()
    expect(within(panel).getByText('Lecturer-Demo-2026!')).toBeInTheDocument()

    await events.click(within(panel).getByRole('button', { name: 'Use the Student demo account' }))
    expect(usernameField()).toHaveValue('S000001')
    expect(passwordField()).toHaveValue('Student-Demo-2026!')
    expect(signIn()).toHaveFocus()
  })

  it('signs in and goes to the role home', async () => {
    const { events, router } = renderLogin()
    await events.type(usernameField(), 'S000001')
    await events.type(passwordField(), 'Student-Demo-2026!')
    await events.click(signIn())
    expect(await screen.findByRole('heading', { name: 'Student home' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/student')
  })

  it('honours a safe returnTo after signing in', async () => {
    const { events } = renderLogin('/login?returnTo=%2Faccount')
    await events.type(usernameField(), 'S000001')
    await events.type(passwordField(), 'Student-Demo-2026!')
    await events.click(signIn())
    expect(await screen.findByRole('heading', { name: 'Account page' })).toBeInTheDocument()
  })

  it('says "Incorrect username or password." and adds the lockout hint from the third failure', async () => {
    const { events } = renderLogin()
    await events.type(usernameField(), 'S000001')
    await events.type(passwordField(), 'wrong')

    await events.click(signIn())
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Incorrect username or password.')
    expect(alert).not.toHaveTextContent('Still stuck?')

    await events.click(signIn())
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('Incorrect username or password.'),
    )
    expect(screen.getByRole('alert')).not.toHaveTextContent('Still stuck?')

    await events.click(signIn())
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Still stuck? Repeated failed attempts can pause sign-in for up to 15 minutes. To reset your password, contact the academic office.',
      ),
    )
  })

  it('shows the wait from Retry-After on 429', async () => {
    const { events } = renderLogin('/login', [
      statusHandler(makeDemoStatus()),
      http.post('/api/auth/login', () =>
        problem('rate-limited', { headers: { 'Retry-After': '30' } }),
      ),
    ])
    await events.type(usernameField(), 'S000001')
    await events.type(passwordField(), 'anything')
    await events.click(signIn())
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Too many attempts. Try again in 30s.',
    )
  })

  it('shows the busy copy on 503', async () => {
    const { events } = renderLogin('/login', [
      statusHandler(makeDemoStatus()),
      http.post('/api/auth/login', () =>
        problem('server-busy', { headers: { 'Retry-After': '1' } }),
      ),
    ])
    await events.type(usernameField(), 'S000001')
    await events.type(passwordField(), 'anything')
    await events.click(signIn())
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The portal is very busy right now. Try again in a moment.',
    )
  })

  it('status 503 still renders a usable form', async () => {
    const { events } = renderLogin('/login', [statusFailureHandler(503)])
    expect(usernameField()).toBeEnabled()
    await waitFor(() => expect(screen.getByText('Student portal')).toBeInTheDocument())
    expect(screen.queryByRole('region', { name: 'Try the demo' })).not.toBeInTheDocument()

    await events.type(usernameField(), 'S000001')
    await events.type(passwordField(), 'Student-Demo-2026!')
    await events.click(signIn())
    expect(await screen.findByRole('heading', { name: 'Student home' })).toBeInTheDocument()
  })

  it('puts the story above the form in demo mode', async () => {
    renderLogin()
    await screen.findByRole('region', { name: 'Try the demo' })
    const quote = screen.getByText(STORY)
    expect(quote.closest('blockquote')).not.toBeNull()
    expect(screen.getByText('Ahmed Ayyan, creator of RushDay').tagName).toBe('CITE')
    // Above the form: in the header, before the sign-in heading, and no "About RushDay" section.
    expect(quote.closest('header')).not.toBeNull()
    expect(screen.queryByRole('heading', { name: 'About RushDay' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'How it holds up under load' })).toHaveAttribute(
      'href',
      '/story',
    )
  })

  it('names the institution and puts the story below the form when demo mode is off', async () => {
    renderLogin('/login', [statusHandler(makePublicStatus())])
    expect(await screen.findByText('Northbridge University student portal')).toBeInTheDocument()
    const about = screen.getByRole('heading', { name: 'About RushDay' })
    const quote = screen.getByText(STORY)
    expect(quote.closest('header')).toBeNull()
    expect(about.compareDocumentPosition(quote) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    const form = signIn().closest('form')
    expect(
      form && form.compareDocumentPosition(quote) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
    expect(screen.queryByRole('region', { name: 'Try the demo' })).not.toBeInTheDocument()
  })

  it('shows the published results line', async () => {
    renderLogin('/login', [
      statusHandler(makePublicStatus({ latestPublication: makePublicationBrief() })),
    ])
    expect(
      await screen.findByText('Autumn 2025/26 results published 28 September 2026 at 10:00 (BST)'),
    ).toBeInTheDocument()
  })

  it('shows the next publication with a countdown', async () => {
    const publishAt = new Date(Date.now() + 26 * 3600_000).toISOString()
    renderLogin('/login', [
      statusHandler(
        makePublicStatus({
          nextPublication: makePublicationBrief({
            academicYear: '2026/27',
            publishAt,
            state: 'scheduled',
          }),
        }),
      ),
    ])
    expect(await screen.findByText(/^Autumn 2026\/27 results publish \d/)).toBeInTheDocument()
    expect(screen.getByText('About 26 hours to go')).toBeInTheDocument()
  })

  it('asks for the verification code when the account has two-step verification', async () => {
    const { events, router } = renderLogin()
    await events.type(usernameField(), 'mfa.admin')
    await events.type(passwordField(), 'Correct-Horse-Battery-9')
    await events.click(signIn())

    expect(
      await screen.findByRole('heading', { name: 'Enter your verification code' }),
    ).toBeInTheDocument()
    expect(
      screen.getByText('Open your authenticator app and type the 6-digit code for RushDay.'),
    ).toBeInTheDocument()
    const code = screen.getByLabelText('Verification code')
    expect(code).toHaveAttribute('autocomplete', 'one-time-code')
    expect(code).toHaveAttribute('inputmode', 'numeric')
    expect(code).toHaveFocus()

    await events.type(code, '000000')
    await events.click(screen.getByRole('button', { name: 'Verify' }))
    expect(
      await screen.findByText(
        "That code didn't work. Check the time on your phone and try the newest code.",
      ),
    ).toBeInTheDocument()

    await events.clear(code)
    await events.type(code, TEST_TOTP_CODE)
    await events.click(screen.getByRole('button', { name: 'Verify' }))
    expect(await screen.findByRole('heading', { name: 'Admin overview' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/admin')
  })

  it('"Start again" returns to the password step', async () => {
    const { events } = renderLogin()
    await events.type(usernameField(), 'mfa.admin')
    await events.type(passwordField(), 'Correct-Horse-Battery-9')
    await events.click(signIn())
    await events.click(await screen.findByRole('button', { name: 'Start again' }))
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('shows the cold-start notice after 3 s of waiting', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const { events } = renderLogin('/login', [
      statusHandler(makeDemoStatus()),
      http.post('/api/auth/login', () => new Promise<Response>(() => {})),
    ])
    await events.type(usernameField(), 'S000001')
    await events.type(passwordField(), 'Student-Demo-2026!')
    await events.click(signIn())
    await waitFor(() => expect(signIn()).toHaveAttribute('aria-busy', 'true'))
    expect(screen.queryByText(/Waking the server/)).not.toBeInTheDocument()
    await vi.advanceTimersByTimeAsync(3_100)
    expect(
      await screen.findByText('Waking the server. On the free tier this can take up to a minute.'),
    ).toBeInTheDocument()
  })

  it('has the footer with the privacy note and the forgotten-password line', async () => {
    renderLogin()
    const footer = screen.getByRole('contentinfo')
    expect(
      within(footer).getByText(
        'Only your session cookie and your theme choice are stored. No third-party scripts.',
      ),
    ).toBeInTheDocument()
    expect(within(footer).getByText(/Forgotten your password\?/)).toHaveTextContent(
      'Forgotten your password? Contact the academic office.',
    )
    expect(within(footer).getByRole('link', { name: 'Accessibility' })).toHaveAttribute(
      'href',
      '/accessibility',
    )
    expect(within(footer).getByRole('link', { name: 'About RushDay' })).toHaveAttribute(
      'href',
      '/story',
    )
    expect(await within(footer).findByText('37c150c')).toBeInTheDocument()
  })
})
