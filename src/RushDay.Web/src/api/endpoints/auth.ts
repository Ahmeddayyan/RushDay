import { apiFetch } from '../client'
import type {
  ChangePasswordRequest,
  CsrfResponse,
  LoginRequest,
  LoginResponse,
  Me,
  MfaChallenge,
  MfaSetupResponse,
} from '../types/auth'

/** The `/api/auth` routes (02-api.md sections 2.3, 2.4, 8.1 and 8.2). AuthProvider is their main caller. */

export function isMfaChallenge(response: LoginResponse): response is MfaChallenge {
  return (response as Partial<MfaChallenge>).mfaRequired === true
}

/** GET /api/auth/me: the signed-in user with a fresh request token. */
export function getMe(signal?: AbortSignal): Promise<Me> {
  return apiFetch<Me>('/api/auth/me', signal ? { signal } : {})
}

/** GET /api/auth/csrf: a request token for the anonymous identity (the sign-in form). */
export function getCsrf(): Promise<CsrfResponse> {
  return apiFetch<CsrfResponse>('/api/auth/csrf')
}

/** POST /api/auth/login → `Me`, or `MfaChallenge` when a code is needed. */
export function login(request: LoginRequest): Promise<LoginResponse> {
  return apiFetch<LoginResponse>('/api/auth/login', { method: 'POST', body: request })
}

/** POST /api/auth/mfa/verify: the second step of sign-in, carried by the rushday.mfa cookie. */
export function verifyMfa(code: string): Promise<Me> {
  return apiFetch<Me>('/api/auth/mfa/verify', { method: 'POST', body: { code } })
}

/** POST /api/auth/logout: rotates the security stamp, so every session of the account ends. */
export function logout(): Promise<void> {
  return apiFetch<void>('/api/auth/logout', { method: 'POST', expect: 'void' })
}

/** POST /api/auth/change-password → 204. */
export function changePassword(request: ChangePasswordRequest): Promise<void> {
  return apiFetch<void>('/api/auth/change-password', {
    method: 'POST',
    body: request,
    expect: 'void',
  })
}

/** POST /api/auth/mfa/setup: a fresh authenticator key (each call replaces the previous one). */
export function setupMfa(): Promise<MfaSetupResponse> {
  return apiFetch<MfaSetupResponse>('/api/auth/mfa/setup', { method: 'POST' })
}

/** POST /api/auth/mfa/enable → `Me` with `mfaEnabled: true` and a fresh token. */
export function enableMfa(code: string): Promise<Me> {
  return apiFetch<Me>('/api/auth/mfa/enable', { method: 'POST', body: { code } })
}
