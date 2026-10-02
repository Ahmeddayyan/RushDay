import { useQuery } from '@tanstack/react-query'

import { studentQueries } from '@/api/endpoints/student'

/**
 * `['student','dashboard']` (`GET /api/me/dashboard`, 60 s fresh): the whole home page in one
 * request (05-frontend.md section 10, `/student`).
 */
export function useDashboard() {
  return useQuery(studentQueries.dashboard())
}

/**
 * The dashboard only if it is already cached; never fetches. The catalogue reads completed modules'
 * marks from it without adding a dashboard request to every catalogue view during an enrolment rush
 * (its data is the catalogue and the enrolments alone, D13).
 */
export function useCachedDashboard() {
  return useQuery({ ...studentQueries.dashboard(), enabled: false })
}
