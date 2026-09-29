import { http, HttpResponse } from 'msw'

import type { LoadResults, LoadRun } from '@/api/types/loadResults'
import type { DemoResetResponse, OpsSnapshot, ReconcileResponse } from '@/api/types/ops'

import { makeOpsSnapshot } from '../factories'
import { problem } from '../http'

/**
 * MSW handlers for the ops routes of 02-api.md section 8.5 and the static load-results document
 * (05-frontend.md section 8, served from `public/data/`, not an `/api` route). `opsMetricsHandler`/
 * `opsMetricsFailureHandler` mirror the `statusHandler`/`statusFailureHandler` pair of
 * `handlers/public.ts` so ops tests can swap in a snapshot or simulate a failed poll with
 * `server.use(...)`. `LoadResults` has no factory in `test/factories.ts` (only `OpsSnapshot` is
 * required there, 05-frontend.md section 13.1): `makeLoadRun`/`makeLoadResults` live here instead,
 * next to the handler that serves them.
 */
export const opsHandlers = [
  http.get('/api/admin/ops/metrics', () => HttpResponse.json(makeOpsSnapshot())),
  http.post('/api/admin/ops/reconcile', () =>
    HttpResponse.json({ modulesCorrected: [] } satisfies ReconcileResponse),
  ),
  http.post('/api/admin/ops/demo-reset', () =>
    HttpResponse.json({ withdrawn: 0 } satisfies DemoResetResponse),
  ),
  http.get('/data/load-results.json', () => HttpResponse.json(makeLoadResults())),
]

export function makeLoadRun(overrides: Partial<LoadRun> = {}): LoadRun {
  return {
    id: 'enrolment-rush-20260927-201228',
    scenario: 'enrolment-rush',
    version: 'v0',
    label: 'Enrolment rush (v0, read-then-write update)',
    ranAt: '2026-09-27T20:12:28',
    source: 'load/results/enrolment-rush-20260927-201228.json',
    notes: '500 students racing for 30 places on CS3099.',
    metrics: {
      requests: 501,
      failedRate: 0.690_618_762_475_049_9,
      p50Ms: 0,
      p95Ms: 729.406,
      maxMs: 929.184_6,
      achievedRate: 512.694_772_846_538_7,
      accepted: 154,
      rejectedFull: 84,
      errored: 262,
      capacity: 30,
      oversold: 124,
    },
    ...overrides,
  }
}

export function makeLoadResults(overrides: Partial<LoadResults> = {}): LoadResults {
  return {
    generatedAt: '2026-09-28T22:02:20.192Z',
    machine: 'Developer laptop: Windows 11, .NET 10, PostgreSQL 18 (native)',
    runs: [makeLoadRun()],
    ...overrides,
  }
}

/** Answers `GET /api/admin/ops/metrics` with `snapshot`. */
export function opsMetricsHandler(snapshot: OpsSnapshot) {
  return http.get('/api/admin/ops/metrics', () => HttpResponse.json(snapshot))
}

/** A poll that fails, as while the server is too busy to answer within the per-user token bucket. */
export function opsMetricsFailureHandler(code: 500 | 503 = 503) {
  return http.get('/api/admin/ops/metrics', () =>
    code === 503
      ? problem('server-busy', { headers: { 'Retry-After': '2' } })
      : problem('internal-error'),
  )
}

export function reconcileHandler(response: ReconcileResponse) {
  return http.post('/api/admin/ops/reconcile', () => HttpResponse.json(response))
}

export function demoResetHandler(response: DemoResetResponse) {
  return http.post('/api/admin/ops/demo-reset', () => HttpResponse.json(response))
}

/** Answers `GET /data/load-results.json` with `results`. */
export function loadResultsHandler(results: LoadResults) {
  return http.get('/data/load-results.json', () => HttpResponse.json(results))
}
