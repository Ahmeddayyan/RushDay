import { useState, type ReactNode } from 'react'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { apiFetch } from '@/api/client'
import type { Me } from '@/api/types/common'
import { AUTH_CHANNEL_NAME } from '@/lib/broadcast'
import { useDirtyForm } from '@/lib/useDirtyForm'
import { makeStudentMe } from '@/test/factories'
import {
  authMock,
  CSRF_ANONYMOUS,
  CSRF_MFA,
  CSRF_SIGNED_IN,
  setSessionUser,
  TEST_TOTP_CODE,
} from '@/test/handlers/auth'
import { problem } from '@/test/http'
import { createTestQueryClient } from '@/test/render'
import { server } from '@/test/server'
import { TestFrame } from '@/test/TestFrame'

import { BOOT_GIVE_UP_MS, useAuth } from './AuthProvider'
import { BootSplash } from './guards'

function Probe() {
  const { status, user, csrf, endReason } = useAuth()
  return (
    <p data-testid="probe">
      {status}|{user?.username ?? ''}|{csrf ?? ''}|{endReason ?? ''}
    </p>
  )
}

const probe = () => screen.getByTestId('probe').textContent

function Actions() {
  const auth = useAuth()
  const [result, setResult] = useState('')
  const run = (action: () => Promise<unknown>) => () => {
    action().then(
      (value) =>
        setResult(
          value === undefined ? 'ok' : typeof value === 'string' ? value : JSON.stringify(value),
        ),
      (error: unknown) => setResult(`error:${(error as { status?: number }).status ?? '?'}`),
    )
  }
  return (
    <>
      <button onClick={run(() => auth.login('mfa.admin', 'Correct-Horse-Battery-9'))}>
        login mfa
      </button>
      <button onClick={run(() => auth.verifyMfa('000000'))}>wrong code</button>
      <button onClick={run(() => auth.verifyMfa(TEST_TOTP_CODE))}>right code</button>
      <button onClick={() => auth.cancelMfa()}>start again</button>
      <button onClick={run(() => auth.logout())}>logout</button>
      <button onClick={run(() => auth.refreshUser())}>refresh</button>
      <p data-testid="result">{result}</p>
    </>
  )
}

function DirtyForm() {
  useDirtyForm(true)
  const [saved, setSaved] = useState('not saved')
  return (
    <>
      <label>
        Mark <input defaultValue="67" />
      </label>
      <button
        onClick={() => {
          apiFetch<{ saved: number }>('/api/lecturer/modules/CS3001/marks', {
            method: 'PUT',
            body: { rows: [] },
          }).then(
            (response) => setSaved(`saved ${response.saved}`),
            () => setSaved('save failed'),
          )
        }}
      >
        Save marks
      </button>
      <p data-testid="saved">{saved}</p>
    </>
  )
}

function renderAuth(
  ui: ReactNode,
  { boot = false, user = makeStudentMe() }: { boot?: boolean; user?: Me | null } = {},
) {
  const client = createTestQueryClient()
  setSessionUser(user)
  render(
    <TestFrame client={client} {...(boot ? {} : { user })}>
      <MemoryRouter>
        <Probe />
        {ui}
      </MemoryRouter>
    </TestFrame>,
  )
  return client
}

afterEach(() => {
  vi.useRealTimers()
})

describe('AuthProvider boot', () => {
  it('goes to authenticated on a 200 from /api/auth/me', async () => {
    renderAuth(null, { boot: true })
    expect(probe()).toMatch(/^booting/)
    await waitFor(() => expect(probe()).toBe(`authenticated|S000001|${CSRF_SIGNED_IN}|`))
  })

  it('goes to anonymous on a 401 and fetches an anonymous token', async () => {
    renderAuth(null, { boot: true, user: null })
    await waitFor(() => expect(probe()).toBe(`anonymous||${CSRF_ANONYMOUS}|`))
  })

  it('keeps booting through network errors and retries with backoff', async () => {
    let calls = 0
    server.use(
      http.get('/api/auth/me', () => {
        calls += 1
        return calls === 1
          ? HttpResponse.error()
          : HttpResponse.json({ ...makeStudentMe(), csrfToken: 'after-retry' })
      }),
    )
    renderAuth(null, { boot: true })
    await waitFor(() => expect(calls).toBe(1))
    expect(probe()).toMatch(/^booting/)
    await waitFor(() => expect(probe()).toBe('authenticated|S000001|after-retry|'), {
      timeout: 3_000,
    })
  })

  it('shows the cold-start notice after 3 s and an error with Retry after 75 s', async () => {
    vi.useFakeTimers()
    server.use(http.get('/api/auth/me', () => new Promise<Response>(() => {})))
    renderAuth(<BootSplash />, { boot: true })

    expect(screen.getByText('Loading RushDay')).toBeInTheDocument()
    expect(screen.queryByText(/Waking the server/)).not.toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(2_999)
    })
    expect(screen.queryByText(/Waking the server/)).not.toBeInTheDocument()
    act(() => {
      vi.advanceTimersByTime(1)
    })
    expect(
      screen.getByText('Waking the server. On the free tier this can take up to a minute.'),
    ).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(BOOT_GIVE_UP_MS)
    })
    expect(screen.getByRole('heading', { name: "The server isn't answering" })).toBeInTheDocument()

    // Retry starts again from the top.
    vi.useRealTimers()
    server.use(
      http.get('/api/auth/me', () => HttpResponse.json({ ...makeStudentMe(), csrfToken: 't' })),
    )
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))
    await waitFor(() => expect(probe()).toMatch(/^authenticated/))
  })
})

describe('AuthProvider sign-in and sign-out', () => {
  it('moves to mfaPending on an MFA challenge, keeps it on a wrong code, and verifies', async () => {
    renderAuth(<Actions />, { user: null })
    const events = userEvent.setup()

    await events.click(screen.getByRole('button', { name: 'login mfa' }))
    await waitFor(() => expect(probe()).toBe(`mfaPending||${CSRF_MFA}|`))

    await events.click(screen.getByRole('button', { name: 'wrong code' }))
    await waitFor(() => expect(screen.getByTestId('result')).toHaveTextContent('error:401'))
    expect(probe()).toMatch(/^mfaPending/)

    await events.click(screen.getByRole('button', { name: 'right code' }))
    await waitFor(() => expect(probe()).toBe(`authenticated|mfa.admin|${CSRF_SIGNED_IN}|`))
  })

  it('returns to anonymous on "Start again"', async () => {
    renderAuth(<Actions />, { user: null })
    const events = userEvent.setup()
    await events.click(screen.getByRole('button', { name: 'login mfa' }))
    await waitFor(() => expect(probe()).toMatch(/^mfaPending/))
    await events.click(screen.getByRole('button', { name: 'start again' }))
    expect(probe()).toMatch(/^anonymous/)
  })

  it('logout clears the cache, fetches a fresh anonymous token and tells the other tabs', async () => {
    const other = new BroadcastChannel(AUTH_CHANNEL_NAME)
    const messages: unknown[] = []
    other.onmessage = (event: MessageEvent<unknown>) => messages.push(event.data)

    const client = renderAuth(<Actions />)
    client.setQueryData(['student', 'dashboard'], { fullName: 'Aisha Khan' })
    await userEvent.click(screen.getByRole('button', { name: 'logout' }))

    await waitFor(() => expect(probe()).toBe(`anonymous||${CSRF_ANONYMOUS}|logout`))
    expect(client.getQueryData(['student', 'dashboard'])).toBeUndefined()
    expect(authMock.user).toBeNull()
    await waitFor(() => expect(messages).toEqual([{ type: 'logout' }]))
    other.close()
  })

  it('signs this tab out when another tab signs out', async () => {
    const client = renderAuth(null)
    client.setQueryData(['student', 'results'], { semesters: [] })
    const other = new BroadcastChannel(AUTH_CHANNEL_NAME)
    other.postMessage({ type: 'logout' })
    await waitFor(() => expect(probe()).toMatch(/^anonymous\|\|.*\|logout$/))
    expect(client.getQueryData(['student', 'results'])).toBeUndefined()
    other.close()
  })

  it('refreshUser adopts the server copy of Me', async () => {
    renderAuth(<Actions />)
    authMock.user = { ...makeStudentMe(), displayName: 'Renamed', mustChangePassword: true }
    await userEvent.click(screen.getByRole('button', { name: 'refresh' }))
    await waitFor(() =>
      expect(screen.getByTestId('result')).toHaveTextContent('"displayName":"Renamed"'),
    )
    expect(probe()).toMatch(/^authenticated/)
  })
})

describe('AuthProvider and expired sessions', () => {
  it('without unsaved work: goes anonymous with one toast and remembers why', async () => {
    server.use(http.get('/api/me/dashboard', () => problem('unauthenticated')))
    const client = renderAuth(null)
    client.setQueryData(['student', 'timetable'], [])

    await Promise.allSettled([apiFetch('/api/me/dashboard'), apiFetch('/api/me/dashboard')])

    await waitFor(() => expect(probe()).toBe(`anonymous||${CSRF_ANONYMOUS}|expired`))
    expect(client.getQueryData(['student', 'timetable'])).toBeUndefined()
    expect(await screen.findAllByText('Your session has ended. Sign in again.')).toHaveLength(1)
  })

  it('with unsaved work: opens ReauthDialog, and after signing in again the save goes through', async () => {
    server.use(
      http.put('/api/lecturer/modules/CS3001/marks', ({ request }) => {
        if (!authMock.user) return problem('unauthenticated')
        return request.headers.get('X-CSRF-TOKEN') === CSRF_SIGNED_IN
          ? HttpResponse.json({ saved: 1 })
          : problem('antiforgery')
      }),
    )
    renderAuth(<DirtyForm />, { user: makeStudentMe({ csrfToken: CSRF_SIGNED_IN }) })
    const events = userEvent.setup()

    // The server-side session ends (idle timeout, security stamp) while the grid has changes.
    authMock.user = null
    await events.click(screen.getByRole('button', { name: 'Save marks' }))

    const dialog = await screen.findByRole('dialog', { name: 'Sign in again to keep your changes' })
    expect(screen.getByTestId('saved')).toHaveTextContent('save failed')
    expect(probe()).toMatch(/^authenticated/)
    expect(screen.getByLabelText('Username')).toHaveValue('S000001')

    await events.type(screen.getByLabelText('Password'), 'Wrong-Password-1')
    await events.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('Incorrect password.')).toBeInTheDocument()

    await events.clear(screen.getByLabelText('Password'))
    await events.type(screen.getByLabelText('Password'), 'Current-Password-1')
    await events.click(screen.getByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(dialog).not.toBeInTheDocument())
    expect(
      await screen.findByText('Signed in again. Press Save to keep your changes.'),
    ).toBeInTheDocument()
    // The unsaved value is still on the page.
    expect(screen.getByLabelText('Mark')).toHaveValue('67')

    await events.click(screen.getByRole('button', { name: 'Save marks' }))
    await waitFor(() => expect(screen.getByTestId('saved')).toHaveTextContent('saved 1'))
  })

  it('asks for a code in ReauthDialog when the account has two-step verification', async () => {
    server.use(http.put('/api/lecturer/modules/CS3001/marks', () => problem('unauthenticated')))
    renderAuth(<DirtyForm />, { user: makeStudentMe({ username: 'mfa.admin', role: 'Admin' }) })
    const events = userEvent.setup()
    authMock.user = null
    await events.click(screen.getByRole('button', { name: 'Save marks' }))
    await screen.findByRole('dialog')

    await events.type(screen.getByLabelText('Password'), 'Current-Password-1')
    await events.click(screen.getByRole('button', { name: 'Sign in' }))
    const code = await screen.findByLabelText('Verification code')
    await events.type(code, TEST_TOTP_CODE)
    await events.click(screen.getByRole('button', { name: 'Verify' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(probe()).toBe(`authenticated|mfa.admin|${CSRF_SIGNED_IN}|`)
  })

  it('Cancel in ReauthDialog takes the normal path', async () => {
    server.use(http.put('/api/lecturer/modules/CS3001/marks', () => problem('unauthenticated')))
    renderAuth(<DirtyForm />)
    authMock.user = null
    await userEvent.click(screen.getByRole('button', { name: 'Save marks' }))
    await screen.findByRole('dialog')
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(probe()).toMatch(/^anonymous\|\|.*\|expired$/))
  })

  it('refetches Me when the server reports a gate the SPA did not know about', async () => {
    server.use(http.get('/api/admin/overview', () => problem('mfa-setup-required')))
    renderAuth(null)
    authMock.user = { ...makeStudentMe(), username: 'gated.admin', mfaSetupRequired: true }
    await expect(apiFetch('/api/admin/overview')).rejects.toMatchObject({ status: 403 })
    await waitFor(() => expect(probe()).toMatch(/gated.admin/))
  })

  it('refreshes the token through /api/auth/me on 400 antiforgery and retries', async () => {
    server.use(
      http.post('/api/me/enrolments', ({ request }) =>
        request.headers.get('X-CSRF-TOKEN') === CSRF_SIGNED_IN
          ? HttpResponse.json({ ok: true })
          : problem('antiforgery'),
      ),
    )
    renderAuth(null, { user: makeStudentMe({ csrfToken: 'stale-token' }) })
    await expect(apiFetch('/api/me/enrolments', { method: 'POST', body: {} })).resolves.toEqual({
      ok: true,
    })
    await waitFor(() => expect(probe()).toBe(`authenticated|S000001|${CSRF_SIGNED_IN}|`))
  })

  it('useAuth throws outside an AuthProvider', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    expect(() => render(<Probe />)).toThrow('useAuth must be used inside <AuthProvider>.')
  })
})
