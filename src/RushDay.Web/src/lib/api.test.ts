import { describe, expect, it } from 'vitest'

import { jsonResponse, mockFetch, problemResponse } from '@/test/mockFetch'

import { ApiError, NetworkError, UnauthorizedError, api } from './api'

describe('api client', () => {
  it('maps a ProblemDetails response to an ApiError with status, title and detail', async () => {
    mockFetch(() => problemResponse(409, 'Module full', 'CS3099 has no places remaining.'))

    const failure = api.post('/students/S000001/enrolments', { moduleCode: 'CS3099' })

    await expect(failure).rejects.toBeInstanceOf(ApiError)
    await expect(failure).rejects.toMatchObject({
      name: 'ApiError',
      status: 409,
      title: 'Module full',
      detail: 'CS3099 has no places remaining.',
      message: 'Module full: CS3099 has no places remaining.',
    })
    await expect(failure).rejects.toHaveProperty('problem.status', 409)
  })

  it('throws UnauthorizedError for 401 so callers can redirect to sign-in', async () => {
    mockFetch(() => problemResponse(401, 'Unauthorized'))

    const failure = api.get('/api/auth/me')

    await expect(failure).rejects.toBeInstanceOf(UnauthorizedError)
    await expect(failure).rejects.toBeInstanceOf(ApiError)
    await expect(failure).rejects.toMatchObject({ status: 401, title: 'Unauthorized' })
  })

  it('falls back to the status text when the error body is not a problem document', async () => {
    mockFetch(() => new Response('<html>nope</html>', { status: 502, statusText: 'Bad Gateway' }))

    await expect(api.get('/modules')).rejects.toMatchObject({
      status: 502,
      title: 'Bad Gateway',
      detail: undefined,
      problem: undefined,
    })
  })

  it('sends JSON with credentials and the X-Requested-With marker', async () => {
    const fetchMock = mockFetch(() => jsonResponse({ ok: true }, 201))

    const result = await api.post<{ ok: boolean }>('/api/auth/login', {
      identifier: 'S000001',
      password: 'pw',
    })

    expect(result).toEqual({ ok: true })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/auth/login')
    expect(init?.method).toBe('POST')
    expect(init?.credentials).toBe('include')
    expect(init?.body).toBe(JSON.stringify({ identifier: 'S000001', password: 'pw' }))
    const headers = new Headers(init?.headers)
    expect(headers.get('X-Requested-With')).toBe('XMLHttpRequest')
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(headers.get('Accept')).toBe('application/json')
  })

  it('returns undefined for an empty 204 response', async () => {
    mockFetch(() => new Response(null, { status: 204 }))

    await expect(api.delete('/api/auth/logout')).resolves.toBeUndefined()
  })

  it('wraps a failed fetch in NetworkError', async () => {
    mockFetch(() => {
      throw new TypeError('Failed to fetch')
    })

    const failure = api.get('/health')

    await expect(failure).rejects.toBeInstanceOf(NetworkError)
    await expect(failure).rejects.toMatchObject({ status: 0, title: 'Network error' })
  })
})
