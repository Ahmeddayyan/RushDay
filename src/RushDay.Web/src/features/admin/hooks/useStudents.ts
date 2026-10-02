import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'

import {
  createStudent,
  getStudent,
  getStudents,
  markStudentLeft,
  overrideEnrol,
  overrideWithdraw,
  updateStudent,
} from '@/api/endpoints/admin'
import { queryKeys } from '@/api/keys'
import type {
  AdminStudentsQuery,
  CreateStudentRequest,
  OverrideEnrolRequest,
  UpdateStudentRequest,
} from '@/api/types/admin'

import { adminGroups, useAdminInvalidate } from './invalidate'

export const STUDENTS_PAGE_SIZE = 25

/** `['admin','students', params]`, keeping the previous page on screen while the next loads. */
export function useStudents(query: AdminStudentsQuery, options: { enabled?: boolean } = {}) {
  const params = {
    q: query.q ?? '',
    accountState: query.accountState ?? null,
    page: query.page ?? 1,
    pageSize: query.pageSize ?? STUDENTS_PAGE_SIZE,
  }
  return useQuery({
    queryKey: queryKeys.admin.students(params),
    queryFn: () =>
      getStudents({
        ...(params.q ? { q: params.q } : {}),
        ...(params.accountState ? { accountState: params.accountState } : {}),
        page: params.page,
        pageSize: params.pageSize,
      }),
    placeholderData: keepPreviousData,
    enabled: options.enabled ?? true,
  })
}

/** `['admin','student', n]`: every fetch is recorded in the audit log as `student.viewed`. */
export function useStudent(studentNumber: string, options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: queryKeys.admin.student(studentNumber),
    queryFn: () => getStudent(studentNumber),
    enabled: options.enabled ?? true,
  })
}

export function useCreateStudent() {
  const invalidate = useAdminInvalidate()
  return useMutation({
    mutationFn: (body: CreateStudentRequest) => createStudent(body),
    onSuccess: () => invalidate(adminGroups.students),
  })
}

export function useUpdateStudent(studentNumber: string) {
  const invalidate = useAdminInvalidate()
  return useMutation({
    mutationFn: (body: UpdateStudentRequest) => updateStudent(studentNumber, body),
    // The linked account's display name changes with the record.
    onSuccess: () => invalidate(adminGroups.students, adminGroups.student, adminGroups.accounts),
  })
}

export function useMarkStudentLeft(studentNumber: string) {
  const invalidate = useAdminInvalidate()
  return useMutation({
    mutationFn: (reason: string) => markStudentLeft(studentNumber, { reason }),
    onSuccess: () =>
      invalidate(
        adminGroups.students,
        adminGroups.student,
        adminGroups.accounts,
        adminGroups.modules,
        adminGroups.module,
        adminGroups.results,
      ),
  })
}

export function useOverrideEnrol(studentNumber: string) {
  const invalidate = useAdminInvalidate()
  return useMutation({
    mutationFn: (body: OverrideEnrolRequest) => overrideEnrol(studentNumber, body),
    onSuccess: () =>
      invalidate(adminGroups.student, adminGroups.modules, adminGroups.module, adminGroups.results),
  })
}

export function useOverrideWithdraw(studentNumber: string) {
  const invalidate = useAdminInvalidate()
  return useMutation({
    mutationFn: ({ code, reason }: { code: string; reason: string }) =>
      overrideWithdraw(studentNumber, code, { reason }),
    onSuccess: () =>
      invalidate(adminGroups.student, adminGroups.modules, adminGroups.module, adminGroups.results),
  })
}
