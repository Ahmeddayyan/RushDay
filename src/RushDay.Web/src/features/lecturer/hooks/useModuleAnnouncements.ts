import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  createModuleAnnouncement,
  deleteModuleAnnouncement,
  getModuleAnnouncements,
  updateModuleAnnouncement,
} from '@/api/endpoints/lecturer'
import { queryKeys } from '@/api/keys'
import type { ModuleAnnouncementRequest } from '@/api/types/lecturer'

/** `GET /api/lecturer/modules/{code}/announcements` (02-api.md section 8.4): future and expired included. */
export function useModuleAnnouncements(code: string) {
  return useQuery({
    queryKey: queryKeys.lecturer.moduleAnnouncements(code),
    queryFn: () => getModuleAnnouncements(code),
  })
}

export function useCreateModuleAnnouncement(code: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: ModuleAnnouncementRequest) => createModuleAnnouncement(code, body),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.lecturer.moduleAnnouncements(code) }),
  })
}

export function useUpdateModuleAnnouncement(code: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ModuleAnnouncementRequest }) =>
      updateModuleAnnouncement(code, id, body),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.lecturer.moduleAnnouncements(code) }),
  })
}

export function useDeleteModuleAnnouncement(code: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteModuleAnnouncement(code, id),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.lecturer.moduleAnnouncements(code) }),
  })
}
