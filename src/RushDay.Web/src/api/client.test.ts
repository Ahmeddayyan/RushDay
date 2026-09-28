import { describe, expect, it, vi } from 'vitest'

import { ApiError, NetworkError, apiFetch, configureClient } from './client'

function noopHooks(overrides: Partial<Parameters<typeof configureClient>[0]> = {}) {
  configureClient({
    getCsrfToken: () => null,
    refreshCsrfToken: async () => {},
    onUnauthenticated: () => {},
    ...overrides,
  })
}

function fetchReturning(response: Response) {
  return vi.fn<typeof fetch>(() => Promise.resolve(response))
}

describe('apiFetch', () => {
  it('sends Accept and Content-Type, same-origin credentials, and returns parsed JSON', async () => {
    const fetchMock = fetchReturning(
      new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)
    noopHooks()

    const result = await apiFetch<{ ok: boolean }>('/api/thing', { method: 'POST', body: { a: 1 } })

    expect(result).toEqual({ ok: true })
    const init = fetchMock.mock.calls.at(0)?.[1]
    const headers = new Headers(init?.headers)
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(headers.get('Accept')).toBe('application/json')
    expect(init?.credentials).toBe('same-origin')
    expect(init?.body).toBe(JSON.stringify({ a: 1 }))
  })

  it('attaches the CSRF header on non-GET requests only', async () => {
    const fetchMock = fetchReturning(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    noopHooks({ getCsrfToken: () => 'tok-123' })

    await apiFetch('/api/get-thing')
    await apiFetch('/api/post-thing', { method: 'POST' })

    const getHeaders = new Headers(fetchMock.mock.calls.at(0)?.[1]?.headers)
    const postHeaders = new Headers(fetchMock.mock.calls.at(1)?.[1]?.headers)
    expect(getHeaders.get('X-CSRF-TOKEN')).toBeNull()
    expect(postHeaders.get('X-CSRF-TOKEN')).toBe('tok-123')
  })

  it('returns undefined for a 204 and for expect: void', async () => {
    vi.stubGlobal('fetch', fetchReturning(new Response(null, { status: 204 })))
    noopHooks()
    await expect(apiFetch('/api/x', { method: 'DELETE' })).resolves.toBeUndefined()
  })

  it('returns a blob with headers when expect is blob', async () => {
    vi.stubGlobal(
      'fetch',
      fetchReturning(new Response(new Blob(['csv']), { status: 200, headers: { 'Content-Type': 'text/csv' } })),
    )
    noopHooks()

    const result = await apiFetch<{ blob: Blob; headers: Headers }>('/api/export.csv', {
      expect: 'blob',
    })

    expect(result.blob).toBeInstanceOf(Blob)
    expect(result.headers.get('Content-Type')).toBe('text/csv')
  })

  it('throws ApiError with the problem, kind and retryAfterSeconds on a non-ok response', async () => {
    vi.stubGlobal(
      'fetch',
      fetchReturning(
        new Response(
          JSON.stringify({
            type: 'urn:rushday:module-full',
            title: 'Module full',
            detail: 'CS3099 has no places remaining.',
            status: 409,
          }),
          { status: 409, headers: { 'Content-Type': 'application/problem+json', 'Retry-After': '5' } },
        ),
      ),
    )
    noopHooks()

    const failure = apiFetch('/api/me/enrolments', { method: 'POST' })

    await expect(failure).rejects.toBeInstanceOf(ApiError)
    await expect(failure).rejects.toMatchObject({
      status: 409,
      kind: 'module-full',
      retryAfterSeconds: 5,
    })
  })

  it('calls onUnauthenticated for a 401 outside the auth-flow paths', async () => {
    const onUnauthenticated = vi.fn()
    vi.stubGlobal('fetch', fetchReturning(new Response(null, { status: 401 })))
    noopHooks({ onUnauthenticated })

    await expect(apiFetch('/api/me/dashboard')).rejects.toBeInstanceOf(ApiError)
    expect(onUnauthenticated).toHaveBeenCalledWith('/api/me/dashboard')
  })

  it('does not call onUnauthenticated for a 401 from /api/auth/me', async () => {
    const onUnauthenticated = vi.fn()
    vi.stubGlobal('fetch', fetchReturning(new Response(null, { status: 401 })))
    noopHooks({ onUnauthenticated })

    await expect(apiFetch('/api/auth/me')).rejects.toBeInstanceOf(ApiError)
    expect(onUnauthenticated).not.toHaveBeenCalled()
  })

  it('wraps a failed fetch in NetworkError with status 0', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(() => Promise.reject(new TypeError('Failed to fetch'))),
    )
    noopHooks()

    const failure = apiFetch('/api/health/live')

    await expect(failure).rejects.toBeInstanceOf(NetworkError)
    await expect(failure).rejects.toMatchObject({ status: 0 })
  })

  it('rethrows an AbortError untouched', async () => {
    const abortError = new DOMException('aborted', 'AbortError')
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(() => Promise.reject(abortError)),
    )
    noopHooks()

    await expect(apiFetch('/api/slow')).rejects.toBe(abortError)
  })
})
