import { http, HttpResponse } from 'msw'

import type { PublicStatus } from '@/api/types/public'

import { makeApiIndex, makePublicStatus } from '../factories'
import { problem } from '../http'

/**
 * MSW handlers for the anonymous routes of 02-api.md section 8.1 (`GET /api`, `GET /api/public/status`,
 * health). Tests override the status with `server.use(statusHandler(makeDemoStatus()))`.
 */
export const publicHandlers = [
  http.get('/api', () => HttpResponse.json(makeApiIndex())),
  http.get('/api/public/status', () => HttpResponse.json(makePublicStatus())),
  http.get('/api/health/live', () => HttpResponse.json({ status: 'Healthy' })),
]

/** Answers `GET /api/public/status` with `status`. */
export function statusHandler(status: PublicStatus) {
  return http.get('/api/public/status', () => HttpResponse.json(status))
}

/** `GET /api/public/status` failing, as during a cold start or while the portal sheds load. */
export function statusFailureHandler(code: 500 | 503 = 503) {
  return http.get('/api/public/status', () =>
    code === 503
      ? problem('server-busy', { headers: { 'Retry-After': '1' } })
      : problem('internal-error'),
  )
}
