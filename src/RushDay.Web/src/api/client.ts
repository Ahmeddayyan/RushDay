/**
 * Typed fetch client for the RushDay API (05-frontend.md section 5.3). This is the S3 skeleton:
 * same-origin cookie sessions, the CSRF header on mutations and the ApiError/NetworkError shapes are
 * final; the antiforgery-refresh-and-retry behaviour is added in stage S5 by AuthProvider's hooks.
 */

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  /** ASP.NET Core validation problems put field errors here. */
  errors?: Record<string, string[]>
  traceId?: string
  [extension: string]: unknown
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
    this.kind = problem?.type?.split(':').pop()
    const retryAfter = headers?.get('Retry-After')
    this.retryAfterSeconds = retryAfter && !Number.isNaN(Number(retryAfter)) ? Number(retryAfter) : undefined
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

export interface ClientHooks {
  getCsrfToken(): string | null
  refreshCsrfToken(): Promise<void>
  onUnauthenticated(path: string): void
}

let hooks: ClientHooks | null = null

/** AuthProvider calls this once on mount; tests call it with fakes. The module keeps no other state. */
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

/** Requests that must never trigger the unauthenticated hook: a 401 from them is an ordinary answer. */
const authFlowPaths = new Set(['/api/auth/me', '/api/auth/login', '/api/auth/mfa/verify'])

async function parseProblem(response: Response): Promise<ProblemDetails | undefined> {
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('json')) return undefined
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return undefined
  }
}

export async function apiFetch<T>(path: string, init: ApiFetchOptions = {}): Promise<T> {
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
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw new NetworkError(error)
  }

  if (!response.ok) {
    const problem = await parseProblem(response)
    if (response.status === 401 && !authFlowPaths.has(path)) hooks?.onUnauthenticated(path)
    throw new ApiError(response.status, problem, response.headers)
  }

  if (response.status === 204 || expect === 'void') return undefined as T
  if (expect === 'blob') return { blob: await response.blob(), headers: response.headers } as unknown as T

  const text = await response.text()
  return (text ? (JSON.parse(text) as unknown) : undefined) as T
}
