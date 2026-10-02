/**
 * Typed fetch client for the RushDay API (05-frontend.md section 5.3).
 *
 * - Same-origin cookie session (`credentials: 'same-origin'`); the SPA never reads a cookie.
 * - Every non-GET request carries `X-CSRF-TOKEN` from `getCsrfToken()` (D5).
 * - A 400 `urn:rushday:antiforgery` refreshes the token once (`refreshCsrfToken()`) and retries the
 *   request once; a second failure is thrown like any other error.
 * - A 401 outside the sign-in flow calls `onUnauthenticated(path)`; AuthProvider decides whether
 *   that ends the session or opens ReauthDialog (section 5.1 step 4).
 * - Errors are `ApiError { status, problem, kind, retryAfterSeconds, headers }`; a failed `fetch`
 *   is `NetworkError` (status 0). Mutations are never retried here; query retries are the query
 *   client's job (section 6.1).
 *
 * AuthProvider calls `configureClient` once on mount; tests call it with fakes. The module keeps
 * no other state.
 */

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  /** Validation problems (and `weak-password`) put field errors here, keyed by camelCase property. */
  errors?: Record<string, string[]>
  traceId?: string
  [extension: string]: unknown
}

/** Seconds from a `Retry-After` header: delta-seconds or an HTTP date. */
export function parseRetryAfter(
  value: string | null | undefined,
  now: number = Date.now(),
): number | undefined {
  if (!value) return undefined
  const trimmed = value.trim()
  if (/^\d+$/.test(trimmed)) return Number(trimmed)
  const date = Date.parse(trimmed)
  if (Number.isNaN(date)) return undefined
  return Math.max(0, Math.ceil((date - now) / 1000))
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | undefined
  /** The slug after the last colon of problem.type, e.g. "module-full". */
  readonly kind: string | undefined
  readonly retryAfterSeconds: number | undefined
  readonly headers: Headers | undefined

  constructor(
    status: number,
    problem: ProblemDetails | undefined,
    headers: Headers | undefined,
    options?: ErrorOptions,
  ) {
    super(problem?.detail ?? problem?.title ?? `HTTP ${status}`, options)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.kind = problem?.type ? problem.type.split(':').pop() : undefined
    this.retryAfterSeconds = parseRetryAfter(headers?.get('Retry-After'))
    this.headers = headers
  }
}

/** A failed `fetch` itself: offline, DNS, refused connection. Status is 0 because there was no response. */
export class NetworkError extends ApiError {
  constructor(cause: unknown) {
    super(0, undefined, undefined, { cause })
    this.name = 'NetworkError'
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError
}

export type GateKind = 'password-change-required' | 'mfa-setup-required'

export interface ClientHooks {
  getCsrfToken(): string | null
  refreshCsrfToken(): Promise<void>
  onUnauthenticated(path: string): void
  /**
   * Optional: a 403 `password-change-required` or `mfa-setup-required` means the SPA's copy of `Me`
   * is stale (for example an administrator reset this account's second factor); AuthProvider
   * refetches `/api/auth/me` so the guards send the person to the right gate.
   */
  onGateRequired?(kind: GateKind): void
}

let hooks: ClientHooks | null = null

export function configureClient(nextHooks: ClientHooks): void {
  hooks = nextHooks
}

export type ApiFetchMethod = 'GET' | 'POST' | 'PUT' | 'DELETE'

export interface ApiFetchOptions {
  method?: ApiFetchMethod
  /** Serialised as JSON. Omit for no body. */
  body?: unknown
  signal?: AbortSignal
  expect?: 'json' | 'blob' | 'void'
}

export interface BlobResult {
  blob: Blob
  headers: Headers
}

/** A 401 from these is an ordinary answer (wrong password, not signed in yet), not an expired session. */
const authFlowPaths = new Set(['/api/auth/me', '/api/auth/login', '/api/auth/mfa/verify'])

function pathOf(path: string): string {
  const query = path.search(/[?#]/)
  return query === -1 ? path : path.slice(0, query)
}

async function parseProblem(response: Response): Promise<ProblemDetails | undefined> {
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('json')) return undefined
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return undefined
  }
}

async function send<T>(path: string, init: ApiFetchOptions, isRetry: boolean): Promise<T> {
  const { method = 'GET', body, signal, expect = 'json' } = init

  const headers = new Headers({ Accept: 'application/json' })
  if (body !== undefined) headers.set('Content-Type', 'application/json')
  if (method !== 'GET') {
    const token = hooks?.getCsrfToken()
    if (token) headers.set('X-CSRF-TOKEN', token)
  }

  const requestInit: RequestInit = { method, credentials: 'same-origin', headers }
  if (body !== undefined) requestInit.body = JSON.stringify(body)
  if (signal !== undefined) requestInit.signal = signal

  let response: Response
  try {
    response = await fetch(path, requestInit)
  } catch (error) {
    // An abort is the caller's decision (React Query cancelling a stale query), not a network failure.
    if (signal?.aborted || (error instanceof Error && error.name === 'AbortError')) throw error
    throw new NetworkError(error)
  }

  if (!response.ok) {
    const problem = await parseProblem(response)
    const error = new ApiError(response.status, problem, response.headers)

    if (
      response.status === 400 &&
      error.kind === 'antiforgery' &&
      method !== 'GET' &&
      !isRetry &&
      hooks
    ) {
      try {
        await hooks.refreshCsrfToken()
      } catch {
        throw error
      }
      return send<T>(path, init, true)
    }

    if (response.status === 401 && !authFlowPaths.has(pathOf(path))) hooks?.onUnauthenticated(path)
    if (
      response.status === 403 &&
      (error.kind === 'password-change-required' || error.kind === 'mfa-setup-required')
    ) {
      hooks?.onGateRequired?.(error.kind)
    }
    throw error
  }

  if (response.status === 204 || expect === 'void') return undefined as T
  if (expect === 'blob') return { blob: await response.blob(), headers: response.headers } as T

  const text = await response.text()
  return (text ? (JSON.parse(text) as unknown) : undefined) as T
}

export function apiFetch<T>(path: string, init: ApiFetchOptions = {}): Promise<T> {
  return send<T>(path, init, false)
}
