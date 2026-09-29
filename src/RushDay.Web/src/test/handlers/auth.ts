import { http, HttpResponse } from 'msw'

import type { Me } from '@/api/types/common'

import { makeAdminMe, makeLecturerMe, makeStudentMe } from '../factories'
import { hasCsrf, problem } from '../http'

/**
 * A small in-memory stand-in for the `/api/auth` routes (02-api.md sections 2.3, 2.4, 8.1, 8.2):
 * accounts with passwords, a session, the MFA challenge between password and code, forced password
 * change and MFA setup. `renderWithProviders({ user })` seeds the session; `resetAuthMock()` runs
 * after every test.
 */

export const TEST_TOTP_CODE = '123456'
export const TEST_SHARED_KEY = 'JBSW Y3DP EHPK 3PXP'

export const CSRF_ANONYMOUS = 'csrf-anonymous'
export const CSRF_MFA = 'csrf-mfa-challenge'
export const CSRF_SIGNED_IN = 'csrf-signed-in'

interface MockAccount {
  password: string
  me: () => Me
  /** Sign-in answers `{ mfaRequired: true }` and the code step follows. */
  mfa?: boolean
}

export const mockAccounts: Record<string, MockAccount> = {
  s000001: { password: 'Student-Demo-2026!', me: () => makeStudentMe({ isDemo: true }) },
  l00001: { password: 'Lecturer-Demo-2026!', me: () => makeLecturerMe({ isDemo: true }) },
  admin: {
    password: 'Admin-Demo-2026!',
    me: () => makeAdminMe({ username: 'admin', isDemo: true, mfaEnabled: false }),
  },
  'mfa.admin': {
    password: 'Correct-Horse-Battery-9',
    mfa: true,
    me: () => makeAdminMe({ username: 'mfa.admin', mfaEnabled: true }),
  },
  'new.lecturer': {
    password: 'Temporary-Pass-2026',
    me: () =>
      makeLecturerMe({ username: 'new.lecturer', staffNumber: 'L90001', mustChangePassword: true }),
  },
}

interface AuthMockState {
  user: Me | null
  pendingMfa: Me | null
  passwords: Record<string, string>
}

export const authMock: AuthMockState = { user: null, pendingMfa: null, passwords: {} }

export function resetAuthMock(): void {
  authMock.user = null
  authMock.pendingMfa = null
  authMock.passwords = {}
}

/** Seeds the signed-in session the handlers answer for (null: signed out). */
export function setSessionUser(user: Me | null, password = 'Current-Password-1'): void {
  authMock.user = user
  authMock.pendingMfa = null
  if (user) authMock.passwords[user.username.toLowerCase()] = password
}

function passwordOf(username: string): string | undefined {
  return (
    authMock.passwords[username.toLowerCase()] ?? mockAccounts[username.toLowerCase()]?.password
  )
}

function signedIn(): Me {
  if (!authMock.user) throw new Error('no session')
  return { ...authMock.user, csrfToken: CSRF_SIGNED_IN }
}

export const authHandlers = [
  http.get('/api/auth/me', () =>
    authMock.user ? HttpResponse.json(signedIn()) : problem('unauthenticated'),
  ),

  http.get('/api/auth/csrf', () => HttpResponse.json({ csrfToken: CSRF_ANONYMOUS })),

  http.post('/api/auth/login', async ({ request }) => {
    if (!hasCsrf(request)) return problem('antiforgery')
    const { username, password } = (await request.json()) as { username: string; password: string }
    const account = mockAccounts[username.toLowerCase()]
    if (!account || passwordOf(username) !== password) return problem('invalid-credentials')
    if (account.mfa) {
      authMock.pendingMfa = account.me()
      return HttpResponse.json({ mfaRequired: true, csrfToken: CSRF_MFA })
    }
    authMock.user = account.me()
    return HttpResponse.json(signedIn())
  }),

  http.post('/api/auth/mfa/verify', async ({ request }) => {
    if (!hasCsrf(request)) return problem('antiforgery')
    const { code } = (await request.json()) as { code: string }
    if (!authMock.pendingMfa || code !== TEST_TOTP_CODE) return problem('invalid-credentials')
    authMock.user = authMock.pendingMfa
    authMock.pendingMfa = null
    return HttpResponse.json(signedIn())
  }),

  http.post('/api/auth/logout', ({ request }) => {
    if (!hasCsrf(request)) return problem('antiforgery')
    if (!authMock.user) return problem('unauthenticated')
    authMock.user = null
    return new HttpResponse(null, { status: 204 })
  }),

  http.post('/api/auth/change-password', async ({ request }) => {
    if (!hasCsrf(request)) return problem('antiforgery')
    if (!authMock.user) return problem('unauthenticated')
    if (authMock.user.isDemo)
      return problem('demo-account', { detail: 'Demo accounts are read-only.' })
    const { currentPassword, newPassword } = (await request.json()) as {
      currentPassword: string
      newPassword: string
    }
    if (newPassword === currentPassword) {
      return problem('weak-password', {
        extensions: { errors: { newPassword: ['same-as-current'] } },
      })
    }
    if (passwordOf(authMock.user.username) !== currentPassword)
      return problem('invalid-current-password')
    if (newPassword.toLowerCase().includes('password')) {
      return problem('weak-password', {
        extensions: { errors: { newPassword: ['PasswordBlocked'] } },
      })
    }
    authMock.passwords[authMock.user.username.toLowerCase()] = newPassword
    authMock.user = { ...authMock.user, mustChangePassword: false }
    return new HttpResponse(null, { status: 204 })
  }),

  http.post('/api/auth/mfa/setup', ({ request }) => {
    if (!hasCsrf(request)) return problem('antiforgery')
    if (!authMock.user) return problem('unauthenticated')
    if (authMock.user.isDemo)
      return problem('demo-account', { detail: 'Demo accounts are read-only.' })
    if (authMock.user.mfaEnabled) return problem('mfa-already-enabled')
    const secret = TEST_SHARED_KEY.replaceAll(' ', '')
    return HttpResponse.json({
      sharedKey: TEST_SHARED_KEY,
      otpauthUri: `otpauth://totp/RushDay:${authMock.user.username}?secret=${secret}&issuer=RushDay&digits=6`,
    })
  }),

  http.post('/api/auth/mfa/enable', async ({ request }) => {
    if (!hasCsrf(request)) return problem('antiforgery')
    if (!authMock.user) return problem('unauthenticated')
    if (authMock.user.isDemo)
      return problem('demo-account', { detail: 'Demo accounts are read-only.' })
    const { code } = (await request.json()) as { code: string }
    if (code !== TEST_TOTP_CODE) return problem('invalid-mfa-code')
    authMock.user = { ...authMock.user, mfaEnabled: true, mfaSetupRequired: false }
    return HttpResponse.json(signedIn())
  }),
]
