import { useQuery } from '@tanstack/react-query'

import { apiFetch } from '../client'
import { queryKeys, queryTimings } from '../keys'
import type { ApiIndex, PublicStatus } from '../types/public'

/** The anonymous routes the shell needs before anyone signs in (02-api.md section 8.1). */

/** GET /api */
export function getIndex(): Promise<ApiIndex> {
  return apiFetch<ApiIndex>('/api')
}

/** GET /api/public/status */
export function getPublicStatus(): Promise<PublicStatus> {
  return apiFetch<PublicStatus>('/api/public/status')
}

/** `['index']`: never stale within a page view (the commit cannot change under a running page). */
export function useApiIndex() {
  return useQuery({ queryKey: queryKeys.index, queryFn: getIndex, ...queryTimings.index })
}

/** `['public','status']`, 60 s fresh. */
export function usePublicStatus() {
  return useQuery({
    queryKey: queryKeys.publicStatus,
    queryFn: getPublicStatus,
    ...queryTimings.publicStatus,
  })
}

/**
 * Server clock minus this browser's clock, captured when `['public','status']` resolved
 * (05-frontend.md section 10, results release). Zero until status has loaded.
 */
export function serverClockOffset(
  status: Pick<PublicStatus, 'serverTime'> | undefined,
  fetchedAt: number,
): number {
  if (!status || !fetchedAt) return 0
  const server = Date.parse(status.serverTime)
  return Number.isNaN(server) ? 0 : server - fetchedAt
}

/** `usePublicStatus` plus the server clock offset and the institution's time zone. */
export function useServerClock(): {
  offsetMs: number
  timeZone: string
  status: PublicStatus | undefined
} {
  const { data, dataUpdatedAt } = usePublicStatus()
  return {
    offsetMs: serverClockOffset(data, dataUpdatedAt),
    timeZone: data?.institution.timeZone ?? 'Europe/London',
    status: data,
  }
}
