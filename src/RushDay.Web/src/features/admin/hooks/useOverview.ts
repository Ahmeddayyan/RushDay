import { useQuery } from '@tanstack/react-query'

import { getOverview } from '@/api/endpoints/admin'
import { queryKeys, queryTimings } from '@/api/keys'

/** `['admin','overview']`, refetched every 30 s while mounted. */
export function useAdminOverview() {
  return useQuery({
    queryKey: queryKeys.admin.overview,
    queryFn: getOverview,
    ...queryTimings.adminOverview,
  })
}
