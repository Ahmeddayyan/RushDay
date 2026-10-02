import { queryOptions } from '@tanstack/react-query'

import { apiFetch } from '../client'
import { queryKeys, queryTimings } from '../keys'
import type { MyEnrolment, TimetableEntry } from '../types/common'
import type {
  DashboardResponse,
  EnrolRequest,
  EnrolResponse,
  ResultsResponse,
} from '../types/student'

/**
 * The student routes (02-api.md section 8.3, group `/api/me`). Student routes never carry a student
 * number: the server takes the student from the session, so ownership is structural (D25).
 */

/** GET /api/me/dashboard: one request, five set-based queries on the server (D12). */
export function getDashboard(signal?: AbortSignal): Promise<DashboardResponse> {
  return apiFetch<DashboardResponse>('/api/me/dashboard', signal ? { signal } : {})
}

/** GET /api/me/results */
export function getResults(signal?: AbortSignal): Promise<ResultsResponse> {
  return apiFetch<ResultsResponse>('/api/me/results', signal ? { signal } : {})
}

/** GET /api/me/timetable: the current semester's classes of this year's active enrolments. */
export function getTimetable(signal?: AbortSignal): Promise<TimetableEntry[]> {
  return apiFetch<TimetableEntry[]>('/api/me/timetable', signal ? { signal } : {})
}

/** GET /api/me/enrolments: every academic year, current year first. */
export function getMyEnrolments(signal?: AbortSignal): Promise<MyEnrolment[]> {
  return apiFetch<MyEnrolment[]>('/api/me/enrolments', signal ? { signal } : {})
}

/**
 * POST /api/me/enrolments → 201. Errors: 404 `module-not-found`; 409 `already-enrolled`,
 * `module-full`, `module-inactive`, `enrolment-window-closed`, `results-exist`, `student-left`;
 * 422 `credit-limit-exceeded`. Never retried (05-frontend.md section 6.3).
 */
export function enrol(moduleCode: string): Promise<EnrolResponse> {
  const body: EnrolRequest = { moduleCode }
  return apiFetch<EnrolResponse>('/api/me/enrolments', { method: 'POST', body })
}

/**
 * DELETE /api/me/enrolments/{code} → 204. Errors: 404 `not-enrolled`; 409
 * `withdrawal-deadline-passed`, `results-exist`.
 */
export function withdraw(code: string): Promise<void> {
  return apiFetch<void>(`/api/me/enrolments/${encodeURIComponent(code)}`, {
    method: 'DELETE',
    expect: 'void',
  })
}

/** Query definitions with the keys and freshness rules of 05-frontend.md section 6.2. */
export const studentQueries = {
  /** `['student','dashboard']`, 60 s fresh. */
  dashboard: () =>
    queryOptions({
      queryKey: queryKeys.student.dashboard,
      queryFn: ({ signal }) => getDashboard(signal),
      ...queryTimings.studentDashboard,
    }),
  /** `['student','results']` */
  results: () =>
    queryOptions({
      queryKey: queryKeys.student.results,
      queryFn: ({ signal }) => getResults(signal),
    }),
  /** `['student','timetable']` */
  timetable: () =>
    queryOptions({
      queryKey: queryKeys.student.timetable,
      queryFn: ({ signal }) => getTimetable(signal),
    }),
  /** `['student','enrolments']`, 15 s fresh. */
  enrolments: () =>
    queryOptions({
      queryKey: queryKeys.student.enrolments,
      queryFn: ({ signal }) => getMyEnrolments(signal),
      ...queryTimings.studentEnrolments,
    }),
}
