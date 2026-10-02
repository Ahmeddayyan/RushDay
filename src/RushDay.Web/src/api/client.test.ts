import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { problem } from '@/test/http'
import { server } from '@/test/server'

import {
  ApiError,
  apiFetch,
  configureClient,
  NetworkError,
  parseRetryAfter,
  type ClientHooks,
} from './client'

let token: string | null = 'token-1'
const hooks = {
  getCsrfToken: vi.fn(() => token),
  refreshCsrfToken: vi.fn(() => {
    token = 'token-2'
    return Promise.resolve()
  }),
  onUnauthenticated: vi.fn(),
  onGateRequired: vi.fn(),
} satisfies ClientHooks

beforeEach(() => {
  token = 'token-1'
  hooks.getCsrfToken.mockClear()
  hooks.refreshCsrfToken.mockClear()
  hooks.onUnauthenticated.mockClear()
  hooks.onGateRequired.mockClear()
  configureClient(hooks)
})

describe('apiFetch', () => {
  it('sends same-origin credentials, Accept and a JSON body, and parses the JSON answer', async () => {
    let seen: Request | undefined
    server.use(
      http.post('/api/thing', async ({ request }) => {
        seen = request.clone()
        return HttpResponse.json({ echoed: await request.json() })
      }),
    )

    await expect(apiFetch('/api/thing', { method: 'POST', body: { a: 1 } })).resolves.toEqual({
      echoed: { a: 1 },
    })
    expect(seen?.headers.get('Accept')).toBe('application/json')
    expect(seen?.headers.get('Content-Type')).toBe('application/json')
    expect(seen?.credentials).toBe('same-origin')
  })

  it('attaches X-CSRF-TOKEN to non-GET requests only', async () => {
    const tokens: (string | null)[] = []
    const record = ({ request }: { request: Request }) => {
      tokens.push(request.headers.get('X-CSRF-TOKEN'))
      return new HttpResponse(null, { status: 204 })
    }
    server.use(
      http.get('/api/x', record),
      http.post('/api/x', record),
      http.put('/api/x', record),
      http.delete('/api/x', record),
    )

    await apiFetch('/api/x')
    await apiFetch('/api/x', { method: 'POST' })
    await apiFetch('/api/x', { method: 'PUT', body: {} })
    await apiFetch('/api/x', { method: 'DELETE' })

    expect(tokens).toEqual([null, 'token-1', 'token-1', 'token-1'])
  })

  it('resolves undefined for 204 and for expect: void', async () => {
    server.use(
      http.delete('/api/y', () => new HttpResponse(null, { status: 204 })),
      http.post('/api/z', () => HttpResponse.json({ ignored: true })),
    )
    await expect(apiFetch('/api/y', { method: 'DELETE' })).resolves.toBeUndefined()
    await expect(apiFetch('/api/z', { method: 'POST', expect: 'void' })).resolves.toBeUndefined()
  })

  it('returns a blob with the response headers for expect: blob', async () => {
    server.use(
      http.get(
        '/api/admin/audit/export.csv',
        () =>
          new HttpResponse('occurredAt,action\n', {
            headers: {
              'Content-Type': 'text/csv',
              'Content-Disposition': 'attachment; filename="audit.csv"',
              'X-RushDay-Truncated': 'true',
            },
          }),
      ),
    )

    const { blob, headers } = await apiFetch<{ blob: Blob; headers: Headers }>(
      '/api/admin/audit/export.csv',
      {
        expect: 'blob',
      },
    )
    expect(await blob.text()).toBe('occurredAt,action\n')
    expect(headers.get('X-RushDay-Truncated')).toBe('true')
  })

  it('throws ApiError with the problem, its slug as kind, and Retry-After in seconds', async () => {
    server.use(
      http.post('/api/me/enrolments', () =>
        problem('module-full', {
          detail: 'CS3099 has no places remaining.',
          headers: { 'Retry-After': '5' },
        }),
      ),
    )

    const failure = apiFetch('/api/me/enrolments', {
      method: 'POST',
      body: { moduleCode: 'CS3099' },
    })
    await expect(failure).rejects.toBeInstanceOf(ApiError)
    await expect(failure).rejects.toMatchObject({
      status: 409,
      kind: 'module-full',
      retryAfterSeconds: 5,
      message: 'CS3099 has no places remaining.',
    })
  })

  it('keeps a non-JSON error body as a bare ApiError', async () => {
    server.use(http.get('/api/plain', () => new HttpResponse('Bad gateway', { status: 502 })))
    await expect(apiFetch('/api/plain')).rejects.toMatchObject({
      status: 502,
      kind: undefined,
      problem: undefined,
    })
  })

  it('calls onUnauthenticated for a 401 outside the sign-in flow', async () => {
    server.use(http.get('/api/me/dashboard', () => problem('unauthenticated')))
    await expect(apiFetch('/api/me/dashboard')).rejects.toMatchObject({ status: 401 })
    expect(hooks.onUnauthenticated).toHaveBeenCalledWith('/api/me/dashboard')
  })

  it.each(['/api/auth/me', '/api/auth/login', '/api/auth/mfa/verify'])(
    'does not call onUnauthenticated for a 401 from %s',
    async (path) => {
      server.use(http.all(path, () => problem('invalid-credentials')))
      await expect(
        apiFetch(path, { method: path === '/api/auth/me' ? 'GET' : 'POST' }),
      ).rejects.toMatchObject({
        status: 401,
      })
      expect(hooks.onUnauthenticated).not.toHaveBeenCalled()
    },
  )

  it('refreshes the token and retries once on 400 antiforgery', async () => {
    const seen: (string | null)[] = []
    server.use(
      http.put('/api/lecturer/modules/CS3001/marks', ({ request }) => {
        const header = request.headers.get('X-CSRF-TOKEN')
        seen.push(header)
        return header === 'token-2' ? HttpResponse.json({ saved: 1 }) : problem('antiforgery')
      }),
    )

    await expect(
      apiFetch('/api/lecturer/modules/CS3001/marks', { method: 'PUT', body: { rows: [] } }),
    ).resolves.toEqual({
      saved: 1,
    })
    expect(hooks.refreshCsrfToken).toHaveBeenCalledTimes(1)
    expect(seen).toEqual(['token-1', 'token-2'])
  })

  it('surfaces a second antiforgery failure instead of retrying again', async () => {
    let calls = 0
    server.use(
      http.post('/api/always-stale', () => {
        calls += 1
        return problem('antiforgery')
      }),
    )

    await expect(apiFetch('/api/always-stale', { method: 'POST' })).rejects.toMatchObject({
      kind: 'antiforgery',
    })
    expect(calls).toBe(2)
    expect(hooks.refreshCsrfToken).toHaveBeenCalledTimes(1)
  })

  it('throws the original antiforgery error when the refresh itself fails', async () => {
    hooks.refreshCsrfToken.mockImplementationOnce(() => Promise.reject(new Error('offline')))
    server.use(http.post('/api/stale', () => problem('antiforgery')))
    await expect(apiFetch('/api/stale', { method: 'POST' })).rejects.toMatchObject({
      kind: 'antiforgery',
    })
  })

  it('tells the auth layer about a gate the SPA did not know about', async () => {
    server.use(http.get('/api/admin/overview', () => problem('mfa-setup-required')))
    await expect(apiFetch('/api/admin/overview')).rejects.toMatchObject({ status: 403 })
    expect(hooks.onGateRequired).toHaveBeenCalledWith('mfa-setup-required')
  })

  it('wraps a failed fetch in NetworkError with status 0', async () => {
    server.use(http.get('/api/health/live', () => HttpResponse.error()))
    const failure = apiFetch('/api/health/live')
    await expect(failure).rejects.toBeInstanceOf(NetworkError)
    await expect(failure).rejects.toMatchObject({ status: 0 })
  })

  it('rethrows an abort untouched', async () => {
    server.use(http.get('/api/slow', () => new Promise<Response>(() => {})))
    const controller = new AbortController()
    const failure = apiFetch('/api/slow', { signal: controller.signal })
    controller.abort()
    await expect(failure).rejects.toMatchObject({ name: 'AbortError' })
  })

  it('works before any hooks are configured', async () => {
    configureClient(undefined as unknown as ClientHooks)
    server.use(http.post('/api/early', () => HttpResponse.json({ ok: true })))
    await expect(apiFetch('/api/early', { method: 'POST' })).resolves.toEqual({ ok: true })
  })
})

describe('parseRetryAfter', () => {
  it('reads delta-seconds and HTTP dates', () => {
    const now = Date.parse('2026-09-28T09:00:00Z')
    expect(parseRetryAfter('7', now)).toBe(7)
    expect(parseRetryAfter('Mon, 28 Sep 2026 09:00:30 GMT', now)).toBe(30)
    expect(parseRetryAfter('soon', now)).toBeUndefined()
    expect(parseRetryAfter(null, now)).toBeUndefined()
  })
})
