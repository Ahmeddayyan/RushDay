import { keepPreviousData, useQuery } from '@tanstack/react-query'

import { getRoster } from '@/api/endpoints/lecturer'
import { queryKeys } from '@/api/keys'

/**
 * `GET /api/lecturer/modules/{code}/roster` (02-api.md section 8.4): server-paged (50/page),
 * searched by number prefix or any part of the name. `placeholderData: keepPreviousData` keeps the
 * old page on screen while the next one loads (05-frontend.md section 6.2).
 */
export function useRoster(code: string, params: { q: string; page: number; pageSize?: number }) {
  const pageSize = params.pageSize ?? 50
  return useQuery({
    queryKey: queryKeys.lecturer.roster(code, { q: params.q, page: params.page, pageSize }),
    queryFn: () => getRoster(code, { q: params.q, page: params.page, pageSize }),
    placeholderData: keepPreviousData,
  })
}
