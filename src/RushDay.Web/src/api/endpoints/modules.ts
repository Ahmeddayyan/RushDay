import { queryOptions } from '@tanstack/react-query'

import { apiFetch } from '../client'
import { queryKeys, queryTimings } from '../keys'
import type { ModuleDetail, ModuleSummary } from '../types/common'

/** The module routes (02-api.md section 8.2), readable by any signed-in role. */

/** GET /api/modules: every active module, ordered by code, from the server's 30 s cache (D13). */
export function getCatalogue(signal?: AbortSignal): Promise<ModuleSummary[]> {
  return apiFetch<ModuleSummary[]>('/api/modules', signal ? { signal } : {})
}

/** GET /api/modules/{code}: read uncached, so `enrolledCount` is live; 404 `module-not-found`. */
export function getModule(code: string, signal?: AbortSignal): Promise<ModuleDetail> {
  return apiFetch<ModuleDetail>(
    `/api/modules/${encodeURIComponent(code)}`,
    signal ? { signal } : {},
  )
}

/** While a module's semester is open for enrolment its page polls, so the places left stay live. */
export const MODULE_DETAIL_POLL_MS = queryTimings.moduleDetail.refetchInterval

export const moduleQueries = {
  /** `['modules','catalogue']`, 15 s fresh. */
  catalogue: () =>
    queryOptions({
      queryKey: queryKeys.modules.catalogue,
      queryFn: ({ signal }) => getCatalogue(signal),
      ...queryTimings.catalogue,
    }),
  /**
   * `['modules', code]`, 5 s fresh; refetched every 10 s while enrolment for the module's semester
   * is open and the page is visible (React Query pauses intervals in a hidden tab).
   */
  detail: (code: string) =>
    queryOptions({
      queryKey: queryKeys.modules.detail(code),
      queryFn: ({ signal }) => getModule(code, signal),
      staleTime: queryTimings.moduleDetail.staleTime,
      refetchInterval: (query) =>
        query.state.data?.enrolmentState === 'open' && query.state.data.isActive
          ? MODULE_DETAIL_POLL_MS
          : false,
      refetchIntervalInBackground: false,
    }),
}
