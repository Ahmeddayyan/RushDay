import { useQuery } from '@tanstack/react-query'

import { apiFetch } from '../client'
import { queryKeys, queryTimings } from '../keys'
import type { AnnouncementView } from '../types/common'

/**
 * GET /api/announcements (02-api.md section 8.2): what the signed-in person can see now, pinned
 * first, then newest first, at most 50.
 */
export function getAnnouncements(): Promise<AnnouncementView[]> {
  return apiFetch<AnnouncementView[]>('/api/announcements')
}

export function useAnnouncements() {
  return useQuery({
    queryKey: queryKeys.announcements,
    queryFn: getAnnouncements,
    ...queryTimings.announcements,
  })
}
