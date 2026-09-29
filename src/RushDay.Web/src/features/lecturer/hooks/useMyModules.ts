import { useQuery } from '@tanstack/react-query'

import { getMyModules } from '@/api/endpoints/lecturer'
import { queryKeys } from '@/api/keys'

/** `GET /api/lecturer/modules` (02-api.md section 8.4): every module the signed-in lecturer teaches. */
export function useMyModules() {
  return useQuery({ queryKey: queryKeys.lecturer.modules, queryFn: getMyModules })
}
