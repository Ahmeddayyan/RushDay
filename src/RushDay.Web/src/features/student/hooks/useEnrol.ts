import { useEffect, useState } from 'react'
import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query'

import { ApiError } from '@/api/client'
import { moduleQueries } from '@/api/endpoints/modules'
import { enrol } from '@/api/endpoints/student'
import { queryKeys } from '@/api/keys'
import { describeProblem, isProblem } from '@/api/problem'
import type { ModuleDetail, ModuleSummary, MyEnrolment } from '@/api/types/common'
import type { PublicStatus } from '@/api/types/public'
import type { DashboardResponse, EnrolResponse } from '@/api/types/student'
import { toast } from '@/lib/toast'

import { placesLeft } from '../copy'

/** Busy answers (429, 503 `server-busy`/`timeout`, no response) without `Retry-After` wait this long. */
export const BUSY_FALLBACK_SECONDS = 2

export interface EnrolControl {
  /** Sends `POST /api/me/enrolments` once; ignored while pending or while the busy wait runs. */
  enrol: () => void
  isPending: boolean
  /** When a 409 `module-full` answered this student's attempt (the "Filled …" caption). */
  filledAt: number | null
  /** Seconds left before the student may try again after a busy answer; 0 when none. */
  busySeconds: number
}

function placesPatch<T extends ModuleSummary>(module: T, placesRemaining: number): T {
  // A successful enrolment ran `UPDATE … WHERE enrolled_count < capacity`, so the count is exact.
  return {
    ...module,
    placesRemaining,
    enrolledCount: Math.max(module.enrolledCount, module.capacity - placesRemaining),
  }
}

function patchModule(
  queryClient: QueryClient,
  code: string,
  patch: <T extends ModuleSummary>(module: T) => T,
): void {
  queryClient.setQueryData<ModuleDetail>(queryKeys.modules.detail(code), (old) =>
    old ? patch(old) : old,
  )
  queryClient.setQueryData<ModuleSummary[]>(queryKeys.modules.catalogue, (list) =>
    list?.map((module) => (module.code === code ? patch(module) : module)),
  )
}

function findModule(queryClient: QueryClient, code: string): ModuleSummary | undefined {
  return (
    queryClient
      .getQueryData<ModuleSummary[]>(queryKeys.modules.catalogue)
      ?.find((module) => module.code === code) ??
    queryClient.getQueryData<ModuleDetail>(queryKeys.modules.detail(code))
  )
}

/**
 * The 201 path of 05-frontend.md section 6.3: the new `placesRemaining` goes into the module and
 * catalogue caches (so the card does not snap back to the 30-second server cache) and the full
 * `MyEnrolment` row goes into `['student','enrolments']`, built from the catalogue item.
 */
export function applyEnrolment(
  queryClient: QueryClient,
  code: string,
  result: EnrolResponse,
): void {
  patchModule(queryClient, code, (module) => placesPatch(module, result.placesRemaining))

  const module = findModule(queryClient, code)
  const rows = queryClient.getQueryData<MyEnrolment[]>(queryKeys.student.enrolments)
  const academicYear =
    queryClient.getQueryData<PublicStatus>(queryKeys.publicStatus)?.academicYear ??
    queryClient.getQueryData<DashboardResponse>(queryKeys.student.dashboard)?.academicYear
  if (!module || !rows || !academicYear) return

  const row: MyEnrolment = {
    moduleCode: code,
    title: module.title,
    credits: module.credits,
    semester: module.semester,
    academicYear,
    status: 'active',
    enrolledAt: result.enrolledAt,
    withdrawnAt: null,
    canWithdraw: true,
    withdrawBlockedReason: null,
    withdrawalDeadlineAt: module.withdrawalDeadlineAt,
  }
  // One row per (student, module): a withdrawn or earlier-year row is reactivated, not duplicated.
  queryClient.setQueryData<MyEnrolment[]>(queryKeys.student.enrolments, [
    row,
    ...rows.filter((existing) => existing.moduleCode !== code),
  ])
}

/** No response at all, 429, or 503 (`server-busy`, `timeout`): the student should simply wait. */
export function isBusyError(error: unknown): error is ApiError {
  return (
    error instanceof ApiError &&
    (error.status === 0 || error.status === 429 || error.status === 503)
  )
}

/**
 * Enrolment on one module (05-frontend.md section 6.3). **Not optimistic**: a student in the rush
 * must never see "Enrolled" for a place the server then refuses. Never retried automatically.
 *
 * - 201: caches patched (`applyEnrolment`), toast "You're in: {code}. {n} places left.", then the
 *   enrolments and the dashboard are refetched (not the catalogue).
 * - 409 `module-full`: `filledAt` is set (the button turns "Full", "Filled while you were
 *   enrolling"), the module is marked full at once and `['modules', code]` is refetched for the live
 *   count.
 * - 429, 503 or no response: the button stays "Enrol" but waits `Retry-After` seconds (2 when
 *   absent) with a visible count.
 * - Anything else: a toast with the `describeProblem` sentence.
 */
export function useEnrol(code: string): EnrolControl {
  const queryClient = useQueryClient()
  const [filledAt, setFilledAt] = useState<number | null>(null)
  const [busy, setBusy] = useState<{ until: number; seconds: number } | null>(null)

  const busyUntil = busy?.until
  useEffect(() => {
    if (busyUntil === undefined) return
    const timer = setInterval(() => {
      const left = Math.ceil((busyUntil - Date.now()) / 1000)
      setBusy(left > 0 ? { until: busyUntil, seconds: left } : null)
    }, 250)
    return () => clearInterval(timer)
  }, [busyUntil])

  const mutation = useMutation({
    mutationKey: ['student', 'enrol', code],
    mutationFn: () => enrol(code),
    retry: 0,
    onSuccess: (result) => {
      setFilledAt(null)
      applyEnrolment(queryClient, code, result)
      toast.success(`You're in: ${code}. ${placesLeft(result.placesRemaining)}.`)
      void queryClient.invalidateQueries({ queryKey: queryKeys.student.enrolments })
      void queryClient.invalidateQueries({ queryKey: queryKeys.student.dashboard })
    },
    onError: (error) => {
      if (isProblem(error, 'module-full')) {
        setFilledAt(Date.now())
        patchModule(queryClient, code, (module) => ({
          ...module,
          placesRemaining: 0,
          enrolledCount: Math.max(module.enrolledCount, module.capacity),
        }))
        queryClient
          .fetchQuery({ ...moduleQueries.detail(code), staleTime: 0 })
          .then((detail) =>
            patchModule(queryClient, code, (module) => ({
              ...module,
              enrolledCount: detail.enrolledCount,
              placesRemaining: detail.placesRemaining,
            })),
          )
          .catch(() => {
            // The module is already shown as full; the next catalogue refresh catches up.
          })
        return
      }

      if (isBusyError(error)) {
        const seconds = Math.max(1, error.retryAfterSeconds ?? BUSY_FALLBACK_SECONDS)
        setBusy({ until: Date.now() + seconds * 1000, seconds })
        return
      }

      // The student's own rows or the window changed under the page: bring the rows up to date.
      if (error instanceof ApiError && (error.status === 409 || error.status === 422)) {
        void queryClient.invalidateQueries({ queryKey: queryKeys.student.enrolments })
      }
      const semester = findModule(queryClient, code)?.semester
      toast.error(
        describeProblem(error, {
          code,
          ownSession: true,
          enrolment: true,
          ...(semester ? { semester } : {}),
        }).message,
      )
    },
  })

  return {
    enrol: () => {
      if (mutation.isPending || busy) return
      mutation.mutate()
    },
    isPending: mutation.isPending,
    filledAt,
    busySeconds: busy?.seconds ?? 0,
  }
}
