import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'

import { studentQueries } from '@/api/endpoints/student'
import { queryTimings } from '@/api/keys'

/** How long `['student','enrolments']` counts as fresh enough to disable "Over credit limit". */
export const ENROLMENTS_FRESH_MS = queryTimings.studentEnrolments.staleTime

/**
 * True while `updatedAt` is less than `windowMs` old; flips to false on time, so a control that
 * trusts fresh data (the credit-limit check) stops trusting it without waiting for a re-render.
 */
export function useIsFresh(updatedAt: number, windowMs: number): boolean {
  const [expired, setExpired] = useState(0)
  useEffect(() => {
    if (!updatedAt) return
    const left = Math.max(0, updatedAt + windowMs - Date.now())
    const timer = setTimeout(() => setExpired(updatedAt), left)
    return () => clearTimeout(timer)
  }, [updatedAt, windowMs])
  return updatedAt > 0 && expired !== updatedAt
}

/**
 * `['student','enrolments']` (`GET /api/me/enrolments`, 15 s fresh): every row of every year, which
 * drives each module's enrol control. `isFresh` says whether the rows are recent enough to disable
 * "Over credit limit" client-side (05-frontend.md section 10); the server's 422 stays the authority.
 */
export function useMyEnrolments() {
  const query = useQuery(studentQueries.enrolments())
  const isFresh = useIsFresh(query.dataUpdatedAt, ENROLMENTS_FRESH_MS)
  return { query, isFresh }
}
