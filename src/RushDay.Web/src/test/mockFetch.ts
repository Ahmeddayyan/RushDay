import { vi, type Mock } from 'vitest'

import type { ProblemDetails } from '@/lib/api'

export type FetchHandler = (url: string, init: RequestInit | undefined) => Response | undefined

/** A JSON 200 response. */
export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** An RFC 7807 response as ASP.NET Core sends it. */
export function problemResponse(status: number, title: string, detail?: string): Response {
  const problem: ProblemDetails = { title, status, detail }
  return new Response(JSON.stringify(problem), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

/**
 * Replaces global fetch for the current test. Handlers are tried in order; the first that returns a
 * Response wins. Anything unhandled fails loudly so a test never silently hits the network.
 */
export function mockFetch(...handlers: FetchHandler[]): Mock<typeof fetch> {
  const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    for (const handler of handlers) {
      const response = handler(url, init)
      if (response) return response
    }
    throw new Error(`Unhandled fetch in test: ${init?.method ?? 'GET'} ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/** Handler for a single method + path. */
export function route(method: string, path: string, respond: () => Response): FetchHandler {
  return (url, init) => {
    const requestMethod = (init?.method ?? 'GET').toUpperCase()
    if (requestMethod !== method.toUpperCase()) return undefined
    const pathname = url.startsWith('http') ? new URL(url).pathname : url.split('?')[0]
    return pathname === path ? respond() : undefined
  }
}

/** The state of the API today: no auth endpoints, so the SPA must treat these as signed out. */
export function anonymousSession(): FetchHandler {
  return route('GET', '/api/auth/me', () => problemResponse(401, 'Unauthorized'))
}
