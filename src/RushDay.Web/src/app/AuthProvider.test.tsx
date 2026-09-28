import { act, render, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { AuthProvider, useAuth, type Me } from './AuthProvider'

function requestUrl(input: RequestInfo | URL): string {
  if (typeof input === 'string') return input
  if (input instanceof URL) return input.href
  return input.url
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function problemResponse(status: number, title: string): Response {
  return new Response(JSON.stringify({ title, status }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

const me: Me = {
  id: '1',
  username: 'S000001',
  displayName: 'Ada Lovelace',
  role: 'Student',
  csrfToken: 'boot-token',
  mustChangePassword: false,
  mfaSetupRequired: false,
}

function wrapper({ children }: { children: React.ReactNode }) {
  return <AuthProvider>{children}</AuthProvider>
}

describe('AuthProvider', () => {
  it('boots to authenticated on a 200 from /api/auth/me', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input) => {
        const url = requestUrl(input)
        if (url.includes('/api/auth/me')) return Promise.resolve(jsonResponse(me))
        return Promise.resolve(new Response(null, { status: 404 }))
      }),
    )

    const { result } = renderHook(() => useAuth(), { wrapper })

    expect(result.current.status).toBe('booting')
    await waitFor(() => expect(result.current.status).toBe('authenticated'))
    expect(result.current.user).toEqual(me)
    expect(result.current.csrf).toBe('boot-token')
  })

  it('boots to anonymous and fetches a csrf token on a 401', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input) => {
        const url = requestUrl(input)
        if (url.includes('/api/auth/me')) return Promise.resolve(problemResponse(401, 'Unauthorized'))
        if (url.includes('/api/auth/csrf')) return Promise.resolve(jsonResponse({ token: 'anon-token' }))
        return Promise.resolve(new Response(null, { status: 404 }))
      }),
    )

    const { result } = renderHook(() => useAuth(), { wrapper })

    await waitFor(() => expect(result.current.status).toBe('anonymous'))
    expect(result.current.csrf).toBe('anon-token')
  })

  it('stays anonymous when the csrf fetch also fails after a 401', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input) => {
        const url = requestUrl(input)
        if (url.includes('/api/auth/me')) return Promise.resolve(problemResponse(401, 'Unauthorized'))
        return Promise.reject(new TypeError('Failed to fetch'))
      }),
    )

    const { result } = renderHook(() => useAuth(), { wrapper })
    await waitFor(() => expect(result.current.status).toBe('anonymous'))
  })

  it('goes anonymous on a network error during boot', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(() => Promise.reject(new TypeError('Failed to fetch'))),
    )

    const { result } = renderHook(() => useAuth(), { wrapper })
    await waitFor(() => expect(result.current.status).toBe('anonymous'))
  })

  it('login: an mfaRequired response moves to mfaPending, then verifyMfa authenticates', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input) => {
        const url = requestUrl(input)
        if (url.includes('/api/auth/me')) return Promise.resolve(problemResponse(401, 'Unauthorized'))
        if (url.includes('/api/auth/csrf')) return Promise.resolve(jsonResponse({ token: 'anon-token' }))
        if (url.includes('/api/auth/mfa/verify')) return Promise.resolve(jsonResponse(me))
        if (url.includes('/api/auth/login')) {
          return Promise.resolve(jsonResponse({ mfaRequired: true, csrfToken: 'mfa-token' }))
        }
        return Promise.resolve(new Response(null, { status: 404 }))
      }),
    )

    const { result } = renderHook(() => useAuth(), { wrapper })
    await waitFor(() => expect(result.current.status).toBe('anonymous'))

    await act(async () => {
      await result.current.login('S000001', 'wrong-code-flow')
    })
    expect(result.current.status).toBe('mfaPending')
    expect(result.current.csrf).toBe('mfa-token')

    await act(async () => {
      await result.current.verifyMfa('123456')
    })
    expect(result.current.status).toBe('authenticated')
    expect(result.current.user).toEqual(me)
  })

  it('login: a direct Me response authenticates without an MFA step', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input) => {
        const url = requestUrl(input)
        if (url.includes('/api/auth/me')) return Promise.resolve(problemResponse(401, 'Unauthorized'))
        if (url.includes('/api/auth/csrf')) return Promise.resolve(jsonResponse({ token: 'anon-token' }))
        if (url.includes('/api/auth/login')) return Promise.resolve(jsonResponse(me))
        return Promise.resolve(new Response(null, { status: 404 }))
      }),
    )

    const { result } = renderHook(() => useAuth(), { wrapper })
    await waitFor(() => expect(result.current.status).toBe('anonymous'))

    await act(async () => {
      await result.current.login('S000001', 'correct')
    })
    expect(result.current.status).toBe('authenticated')
    expect(result.current.user).toEqual(me)
  })

  it('logout clears the user, goes anonymous and fetches a fresh csrf token', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input, init) => {
        const url = requestUrl(input)
        if (url.includes('/api/auth/me')) return Promise.resolve(jsonResponse(me))
        if (url.includes('/api/auth/logout') && init?.method === 'POST') {
          return Promise.resolve(new Response(null, { status: 204 }))
        }
        if (url.includes('/api/auth/csrf')) return Promise.resolve(jsonResponse({ token: 'fresh-token' }))
        return Promise.resolve(new Response(null, { status: 404 }))
      }),
    )

    const { result } = renderHook(() => useAuth(), { wrapper })
    await waitFor(() => expect(result.current.status).toBe('authenticated'))

    await act(async () => {
      await result.current.logout()
    })
    expect(result.current.status).toBe('anonymous')
    expect(result.current.user).toBeNull()
    expect(result.current.csrf).toBe('fresh-token')
  })

  it('useAuth throws outside an AuthProvider', () => {
    function Consumer() {
      useAuth()
      return null
    }
    expect(() => render(<Consumer />)).toThrow('useAuth must be used inside <AuthProvider>.')
  })
})
