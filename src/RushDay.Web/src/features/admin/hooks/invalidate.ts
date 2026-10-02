import { useCallback } from 'react'
import { useQueryClient, type QueryKey } from '@tanstack/react-query'

import { queryKeys } from '@/api/keys'

/**
 * Prefixes of the admin query groups of 05-frontend.md section 6.2, built from `queryKeys` so an
 * invalidation always matches the keys the queries use (`['admin','students', params]` is matched
 * by `['admin','students']`).
 */
export const adminGroups = {
  overview: queryKeys.admin.overview,
  settings: queryKeys.admin.settings,
  windows: queryKeys.admin.windows,
  results: queryKeys.admin.results('', 'autumn').slice(0, 2),
  lecturers: queryKeys.admin.lecturers('').slice(0, 2),
  students: queryKeys.admin.students({}).slice(0, 2),
  student: queryKeys.admin.student('').slice(0, 2),
  accounts: queryKeys.admin.accounts({}).slice(0, 2),
  audit: queryKeys.admin.audit({}).slice(0, 2),
  modules: queryKeys.admin.modules(false).slice(0, 2),
  module: queryKeys.admin.module('').slice(0, 2),
  announcements: queryKeys.admin.announcements,
} satisfies Record<string, QueryKey>

/**
 * "Admin mutations invalidate their group key and ['admin','overview']" (05-frontend.md section
 * 6.3). Returns a function taking the affected groups (and any other keys, such as
 * `['public','status']` after a window or publication change).
 */
export function useAdminInvalidate() {
  const queryClient = useQueryClient()
  return useCallback(
    (...keys: QueryKey[]) =>
      Promise.all(
        [adminGroups.overview, ...keys].map((queryKey) =>
          queryClient.invalidateQueries({ queryKey }),
        ),
      ),
    [queryClient],
  )
}
