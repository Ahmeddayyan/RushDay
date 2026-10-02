import { useQuery } from '@tanstack/react-query'

import { studentQueries } from '@/api/endpoints/student'

/** `['student','timetable']` (`GET /api/me/timetable`): this year's classes of the current semester. */
export function useTimetable() {
  return useQuery(studentQueries.timetable())
}
