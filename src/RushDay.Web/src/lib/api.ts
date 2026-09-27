/**
 * Typed fetch client for the RushDay API.
 *
 * - Same-origin, cookie-based sessions: every request carries credentials.
 * - X-Requested-With marks the caller as the SPA so the server can answer 401 instead of redirecting,
 *   and, being a custom header, forces a CORS preflight for any cross-site caller.
 * - Failures are RFC 7807 ProblemDetails; they are mapped to ApiError (401 -> UnauthorizedError).
 */

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

/** RFC 7807 / RFC 9457 problem document as ASP.NET Core emits it (extensions allowed). */
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
  readonly title: string
  readonly detail: string | undefined
  readonly problem: ProblemDetails | undefined

  constructor(
    status: number,
    title: string,
    detail?: string,
    problem?: ProblemDetails,
    options?: ErrorOptions,
  ) {
    super(detail ? `${title}: ${detail}` : title, options)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.detail = detail
    this.problem = problem
  }
}

/** The session is missing or expired. Callers usually redirect to /login. */
export class UnauthorizedError extends ApiError {
  constructor(title = 'Unauthorized', detail?: string, problem?: ProblemDetails) {
    super(401, title, detail, problem)
    this.name = 'UnauthorizedError'
  }
}

/** fetch itself failed: offline, DNS, refused connection. status is 0 because there was no response. */
export class NetworkError extends ApiError {
  constructor(cause: unknown) {
    super(
      0,
      'Network error',
      'The server could not be reached. Check your connection and try again.',
      undefined,
      { cause },
    )
    this.name = 'NetworkError'
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError
}

export interface RequestOptions {
  method?: HttpMethod
  /** Serialised as JSON. Pass undefined for no body. */
  body?: unknown
  headers?: HeadersInit
  signal?: AbortSignal
}

const jsonMediaType = /^application\/(problem\+)?json\b/i

async function readBody(response: Response): Promise<unknown> {
  if (response.status === 204 || response.headers.get('content-length') === '0') return undefined
  const text = await response.text()
  if (!text) return undefined
  if (jsonMediaType.test(response.headers.get('content-type') ?? '')) {
    try {
      return JSON.parse(text) as unknown
    } catch {
      return text
    }
  }
  return text
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return (
    typeof value === 'object' &&
    value !== null &&
    ('title' in value || 'detail' in value || 'status' in value)
  )
}

async function toApiError(response: Response): Promise<ApiError> {
  const payload = await readBody(response)
  const problem = isProblemDetails(payload) ? payload : undefined
  const title = problem?.title ?? (response.statusText || `HTTP ${response.status}`)
  const detail = problem?.detail
  if (response.status === 401) return new UnauthorizedError(title, detail, problem)
  return new ApiError(response.status, title, detail, problem)
}

export async function request<T>(
  path: string,
  { method = 'GET', body, headers, signal }: RequestOptions = {},
): Promise<T> {
  const requestHeaders = new Headers(headers)
  if (!requestHeaders.has('Accept')) requestHeaders.set('Accept', 'application/json')
  requestHeaders.set('X-Requested-With', 'XMLHttpRequest')
  if (body !== undefined && !requestHeaders.has('Content-Type')) {
    requestHeaders.set('Content-Type', 'application/json')
  }

  let response: Response
  try {
    response = await fetch(path, {
      method,
      credentials: 'include',
      headers: requestHeaders,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
    })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw new NetworkError(error)
  }

  if (!response.ok) throw await toApiError(response)
  return (await readBody(response)) as T
}

type MethodOptions = Omit<RequestOptions, 'method' | 'body'>

export const api = {
  get: <T>(path: string, options?: MethodOptions) =>
    request<T>(path, { ...options, method: 'GET' }),
  post: <T>(path: string, body?: unknown, options?: MethodOptions) =>
    request<T>(path, { ...options, method: 'POST', body }),
  put: <T>(path: string, body?: unknown, options?: MethodOptions) =>
    request<T>(path, { ...options, method: 'PUT', body }),
  patch: <T>(path: string, body?: unknown, options?: MethodOptions) =>
    request<T>(path, { ...options, method: 'PATCH', body }),
  delete: <T>(path: string, options?: MethodOptions) =>
    request<T>(path, { ...options, method: 'DELETE' }),
}
