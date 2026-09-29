import { useMutation, useQuery } from '@tanstack/react-query'

import {
  createAnnouncement,
  deleteAnnouncement,
  getAdminAnnouncements,
  updateAnnouncement,
} from '@/api/endpoints/admin'
import { queryKeys } from '@/api/keys'
import type { AnnouncementRequest } from '@/api/types/admin'

import { adminGroups, useAdminInvalidate } from './invalidate'

/** `['admin','announcements']`: every scope, including future and expired, not deleted. */
export function useAdminAnnouncements() {
  return useQuery({ queryKey: queryKeys.admin.announcements, queryFn: getAdminAnnouncements })
}

/** Announcements also reach every signed-in person through `['announcements']`. */
function useAnnouncementInvalidate() {
  const invalidate = useAdminInvalidate()
  return () => invalidate(adminGroups.announcements, queryKeys.announcements)
}

export function useSaveAnnouncement() {
  const invalidate = useAnnouncementInvalidate()
  return useMutation({
    mutationFn: ({ id, body }: { id: string | null; body: AnnouncementRequest }) =>
      id === null ? createAnnouncement(body) : updateAnnouncement(id, body),
    onSuccess: invalidate,
  })
}

export function useDeleteAnnouncement() {
  const invalidate = useAnnouncementInvalidate()
  return useMutation({
    mutationFn: (id: string) => deleteAnnouncement(id),
    onSuccess: invalidate,
  })
}
