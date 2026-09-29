import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'

import {
  createLecturer,
  getLecturers,
  markLecturerLeft,
  updateLecturer,
} from '@/api/endpoints/admin'
import { queryKeys } from '@/api/keys'
import type { CreateLecturerRequest, UpdateLecturerRequest } from '@/api/types/admin'

import { adminGroups, useAdminInvalidate } from './invalidate'

/** `['admin','lecturers', q]`: staff number prefix or name fragment. */
export function useLecturers(q: string, options: { enabled?: boolean } = {}) {
  const trimmed = q.trim()
  return useQuery({
    queryKey: queryKeys.admin.lecturers(trimmed),
    queryFn: () => getLecturers(trimmed),
    placeholderData: keepPreviousData,
    enabled: options.enabled ?? true,
  })
}

export function useCreateLecturer() {
  const invalidate = useAdminInvalidate()
  return useMutation({
    mutationFn: (body: CreateLecturerRequest) => createLecturer(body),
    onSuccess: () => invalidate(adminGroups.lecturers),
  })
}

export function useUpdateLecturer() {
  const invalidate = useAdminInvalidate()
  return useMutation({
    mutationFn: ({ staffNumber, body }: { staffNumber: string; body: UpdateLecturerRequest }) =>
      updateLecturer(staffNumber, body),
    // Names show on module rows and on the linked account.
    onSuccess: () =>
      invalidate(
        adminGroups.lecturers,
        adminGroups.modules,
        adminGroups.module,
        adminGroups.accounts,
      ),
  })
}

export function useMarkLecturerLeft() {
  const invalidate = useAdminInvalidate()
  return useMutation({
    mutationFn: ({ staffNumber, reason }: { staffNumber: string; reason: string }) =>
      markLecturerLeft(staffNumber, { reason }),
    onSuccess: () =>
      invalidate(
        adminGroups.lecturers,
        adminGroups.modules,
        adminGroups.module,
        adminGroups.accounts,
      ),
  })
}
