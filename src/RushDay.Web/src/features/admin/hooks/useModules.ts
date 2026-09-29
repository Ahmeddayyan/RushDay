import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'

import {
  createModule,
  getAdminModules,
  getModuleDetail,
  getModuleMarks,
  getModuleRoster,
  setModuleLecturers,
  trimModule,
  updateModule,
} from '@/api/endpoints/admin'
import { queryKeys } from '@/api/keys'
import type {
  CreateModuleRequest,
  LecturerAssignment,
  UpdateModuleRequest,
} from '@/api/types/admin'

import { adminGroups, useAdminInvalidate } from './invalidate'

export const ROSTER_PAGE_SIZE = 50
export const MARKS_PAGE_SIZE = 100

/** `['admin','modules', includeInactive]`: every module with this year's counts and marks status. */
export function useAdminModules(includeInactive: boolean) {
  return useQuery({
    queryKey: queryKeys.admin.modules(includeInactive),
    queryFn: () => getAdminModules(includeInactive),
    placeholderData: keepPreviousData,
  })
}

/** `['modules', code]` (`GET /api/modules/{code}`): live counts and the timetable slots. */
export function useModuleDetail(code: string) {
  return useQuery({
    queryKey: queryKeys.modules.detail(code),
    queryFn: () => getModuleDetail(code),
  })
}

/** Module edits change the catalogue every role sees. */
function useModuleInvalidate() {
  const invalidate = useAdminInvalidate()
  return () =>
    invalidate(
      adminGroups.modules,
      adminGroups.module,
      adminGroups.results,
      adminGroups.lecturers,
      queryKeys.modules.all,
    )
}

export function useCreateModule() {
  const invalidate = useModuleInvalidate()
  return useMutation({
    mutationFn: (body: CreateModuleRequest) => createModule(body),
    onSuccess: invalidate,
  })
}

export function useUpdateModule() {
  const invalidate = useModuleInvalidate()
  return useMutation({
    mutationFn: ({ code, body }: { code: string; body: UpdateModuleRequest }) =>
      updateModule(code, body),
    onSuccess: invalidate,
  })
}

export function useSetLecturers(code: string) {
  const invalidate = useModuleInvalidate()
  return useMutation({
    mutationFn: (assignments: LecturerAssignment[]) => setModuleLecturers(code, { assignments }),
    onSuccess: invalidate,
  })
}

export function useTrimModule(code: string) {
  const invalidate = useModuleInvalidate()
  const invalidateAdmin = useAdminInvalidate()
  return useMutation({
    mutationFn: (reason: string) => trimModule(code, { reason }),
    onSuccess: async () => {
      await invalidate()
      await invalidateAdmin(adminGroups.student)
    },
  })
}

export interface ModulePageParams {
  q: string
  page: number
}

/** `['admin','module', code, 'roster', params]`, 50 per page. */
export function useModuleRoster(code: string, { q, page }: ModulePageParams) {
  const params = { q, page, pageSize: ROSTER_PAGE_SIZE }
  return useQuery({
    queryKey: queryKeys.admin.moduleRoster(code, params),
    queryFn: () => getModuleRoster(code, params),
    placeholderData: keepPreviousData,
  })
}

/** `['admin','module', code, 'marks', params]`, 100 per page, read-only. */
export function useModuleMarks(code: string, { q, page }: ModulePageParams) {
  const params = { q, page, pageSize: MARKS_PAGE_SIZE }
  return useQuery({
    queryKey: queryKeys.admin.moduleMarks(code, params),
    queryFn: () => getModuleMarks(code, params),
    placeholderData: keepPreviousData,
  })
}
