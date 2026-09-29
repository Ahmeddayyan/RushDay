import { HttpResponse } from 'msw'

import type { ProblemSlug } from '@/api/problem'
import { PROBLEM_CATALOGUE } from '@/api/problem'

/**
 * A ProblemDetails response as the API writes it (02-api.md section 6): `type` is
 * `urn:rushday:<slug>`, `traceId` is always present, extensions sit at the top level.
 */
export function problem(
  slug: ProblemSlug,
  {
    status,
    detail,
    extensions,
    headers,
  }: {
    status?: number
    detail?: string
    extensions?: Record<string, unknown>
    headers?: Record<string, string>
  } = {},
) {
  const code = status ?? PROBLEM_CATALOGUE[slug]
  return HttpResponse.json(
    {
      type: `urn:rushday:${slug}`,
      title: slug.charAt(0).toUpperCase() + slug.slice(1).replaceAll('-', ' '),
      status: code,
      detail: detail ?? null,
      instance: null,
      traceId: '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01',
      ...extensions,
    },
    { status: code, headers: { 'Content-Type': 'application/problem+json', ...headers } },
  )
}

/** True when a mutation carried a request token, as the API's antiforgery filter requires. */
export function hasCsrf(request: Request): boolean {
  return Boolean(request.headers.get('X-CSRF-TOKEN'))
}
