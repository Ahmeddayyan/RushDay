import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'

import {
  cancelPublication,
  correctMark,
  getResults,
  publishResults,
  reschedulePublication,
  returnModuleToDraft,
  unpublishPublication,
} from '@/api/endpoints/admin'
import { queryKeys } from '@/api/keys'
import type { CorrectMarkRequest, PublishRequest, ReturnToDraftRequest } from '@/api/types/admin'
import type { Semester } from '@/api/types/common'

import { adminGroups, useAdminInvalidate } from './invalidate'

/** `['admin','results', academicYear, semester]`: submission progress and publication history. */
export function useAdminResults(academicYear: string, semester: Semester) {
  return useQuery({
    queryKey: queryKeys.admin.results(academicYear, semester),
    queryFn: () => getResults({ semester, academicYear }),
    enabled: academicYear !== '',
    placeholderData: keepPreviousData,
  })
}

/**
 * Every results mutation changes what students will see (`['public','status']` carries the next and
 * latest publication) and the marks status shown on the module and student pages.
 */
function useResultsInvalidate() {
  const invalidate = useAdminInvalidate()
  return () =>
    invalidate(
      adminGroups.results,
      adminGroups.modules,
      adminGroups.module,
      adminGroups.student,
      adminGroups.announcements,
      queryKeys.publicStatus,
    )
}

export function usePublishResults() {
  const invalidate = useResultsInvalidate()
  return useMutation({
    mutationFn: (body: PublishRequest) => publishResults(body),
    onSuccess: invalidate,
  })
}

export function useReschedulePublication() {
  const invalidate = useResultsInvalidate()
  return useMutation({
    mutationFn: ({ id, publishAt }: { id: string; publishAt: string }) =>
      reschedulePublication(id, { publishAt }),
    onSuccess: invalidate,
  })
}

export function useCancelPublication() {
  const invalidate = useResultsInvalidate()
  return useMutation({
    mutationFn: (id: string) => cancelPublication(id),
    onSuccess: invalidate,
  })
}

export function useUnpublish() {
  const invalidate = useResultsInvalidate()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      unpublishPublication(id, { reason }),
    onSuccess: invalidate,
  })
}

export function useReturnToDraft() {
  const invalidate = useResultsInvalidate()
  return useMutation({
    mutationFn: ({ code, body }: { code: string; body: ReturnToDraftRequest }) =>
      returnModuleToDraft(code, body),
    onSuccess: invalidate,
  })
}

export function useCorrectMark() {
  const invalidate = useResultsInvalidate()
  return useMutation({
    mutationFn: ({
      code,
      studentNumber,
      body,
    }: {
      code: string
      studentNumber: string
      body: CorrectMarkRequest
    }) => correctMark(code, studentNumber, body),
    onSuccess: invalidate,
  })
}
