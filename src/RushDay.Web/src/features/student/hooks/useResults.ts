import { useQuery } from '@tanstack/react-query'

import { studentQueries } from '@/api/endpoints/student'

/** `['student','results']` (`GET /api/me/results`): every (year, semester) with visible marks. */
export function useResults() {
  return useQuery(studentQueries.results())
}
