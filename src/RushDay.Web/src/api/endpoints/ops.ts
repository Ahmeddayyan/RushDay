import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { apiFetch } from '../client'
import { queryKeys, queryTimings } from '../keys'
import type { LoadResults } from '../types/loadResults'
import type { DemoResetResponse, OpsSnapshot, ReconcileResponse } from '../types/ops'

/**
 * Admin ops routes (02-api.md section 8.5) and the static load-results document
 * (05-frontend.md section 8). `useOpsMetrics` polls every 5 s while `/admin/ops` is visible
 * (`queryTimings.opsMetrics`: `refetchIntervalInBackground: false` pauses it while the tab is
 * hidden) and, like any TanStack Query, keeps the last successful `data` on screen when a poll
 * fails: `OpsPage` reads `isError`/`dataUpdatedAt` alongside `data` to greet a failed sample without
 * throwing the whole page into an error state.
 */

/** GET /api/admin/ops/metrics */
export function getOpsMetrics(): Promise<OpsSnapshot> {
  return apiFetch<OpsSnapshot>('/api/admin/ops/metrics')
}

export function useOpsMetrics() {
  return useQuery({
    queryKey: queryKeys.admin.opsMetrics,
    queryFn: getOpsMetrics,
    ...queryTimings.opsMetrics,
  })
}

/** POST /api/admin/ops/reconcile */
export function reconcile(): Promise<ReconcileResponse> {
  return apiFetch<ReconcileResponse>('/api/admin/ops/reconcile', { method: 'POST' })
}

export function useReconcile() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: reconcile,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.admin.opsMetrics })
    },
  })
}

/** POST /api/admin/ops/demo-reset; mapped only when Demo:Enabled. */
export function demoReset(): Promise<DemoResetResponse> {
  return apiFetch<DemoResetResponse>('/api/admin/ops/demo-reset', { method: 'POST' })
}

export function useDemoReset() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: demoReset,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.admin.opsMetrics })
    },
  })
}

/**
 * GET /data/load-results.json: a static file, not an `/api` route, so it goes through a plain
 * `fetch` rather than `apiFetch` (no CSRF header, no ProblemDetails envelope; a non-2xx or a bad
 * body is thrown as a plain `Error` and surfaced by `describeProblem`'s generic fallback).
 */
export async function getLoadResults(): Promise<LoadResults> {
  const response = await fetch('/data/load-results.json', {
    headers: { Accept: 'application/json' },
  })
  if (!response.ok) {
    throw new Error(`Couldn't load the load-test results (HTTP ${response.status}).`)
  }
  return (await response.json()) as LoadResults
}

export function useLoadResults() {
  return useQuery({
    queryKey: queryKeys.loadResults,
    queryFn: getLoadResults,
    ...queryTimings.loadResults,
  })
}
