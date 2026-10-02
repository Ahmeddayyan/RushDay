import { keepPreviousData, useQuery } from '@tanstack/react-query'

import { getMarks } from '@/api/endpoints/lecturer'
import { queryKeys } from '@/api/keys'

/** `MARKS_PAGE_SIZE`: 02-api.md section 8.4 caps the marks route at 500; 100/page is the default UI page. */
export const MARKS_PAGE_SIZE = 100

/**
 * `GET /api/lecturer/modules/{code}/marks` (02-api.md section 8.4): the page's rows plus a
 * whole-module `summary`, searched and paged; `keepPreviousData` avoids a flash to empty between pages.
 */
export function useMarks(code: string, params: { q: string; page: number }) {
  return useQuery({
    queryKey: queryKeys.lecturer.marks(code, params),
    queryFn: () => getMarks(code, { q: params.q, page: params.page }),
    placeholderData: keepPreviousData,
  })
}

/**
 * A one-off, larger read used only to list every student still missing a mark when the leader opens
 * `SubmitDialog` (up to the route's 500-row cap): a separate, unlisted query key on purpose, so it
 * never collides with the paged grid's cache entry for the same module and page 1.
 */
export function useMissingStudents(code: string, enabled: boolean) {
  return useQuery({
    queryKey: ['lecturer', 'module', code, 'marks-missing-preview'] as const,
    queryFn: async () => {
      const sheet = await getMarks(code, { q: '', page: 1, pageSize: 500 })
      return sheet.rows
        .filter((row) => row.enrolmentStatus === 'active' && row.outcome === null)
        .map((row) => ({ studentNumber: row.studentNumber, fullName: row.fullName }))
    },
    enabled,
    placeholderData: keepPreviousData,
  })
}
