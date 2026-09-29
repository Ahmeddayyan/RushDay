import type { ModuleSummary, MyEnrolment, Semester } from '@/api/types/common'

/**
 * What the enrol control shows for one module (05-frontend.md section 10, `EnrolButton` states),
 * derived from the module and the student's row in `['student','enrolments']` (the row of any year
 * for that module; the unique (student, module) index allows one). The pending, "filled while you
 * were enrolling" and busy states are the mutation's and sit on top of this (`useEnrol`).
 */

export const CREDIT_LIMIT = 60

export type EnrolStateKind =
  /** Active this year and still withdrawable: "Enrolled" + "Withdraw". */
  | 'enrolled'
  /** Active this year, a submitted or published mark exists. */
  | 'enrolledResults'
  /** Active this year, the withdrawal deadline has passed. */
  | 'enrolledDeadline'
  /** Active in an earlier academic year. */
  | 'completed'
  | 'inactive'
  | 'full'
  | 'notYetOpen'
  | 'closed'
  | 'noWindow'
  /** This module would take the semester past 60 credits. */
  | 'overCredits'
  /** A withdrawn row, window open, places left. */
  | 'enrolAgain'
  | 'enrol'

export interface EnrolState {
  kind: EnrolStateKind
  /** `overCredits` only: disabled while the enrolments are fresh; the server's 422 stays the authority. */
  disabled: boolean
  /** The row is withdrawn, so the action reads "Enrol again". */
  again: boolean
}

export type EnrolStateModule = Pick<
  ModuleSummary,
  'code' | 'credits' | 'semester' | 'isActive' | 'placesRemaining' | 'enrolmentState'
>

/** A row of the current academic year. Without the year from status, the server's reason tells. */
export function isCurrentYearRow(
  row: Pick<MyEnrolment, 'academicYear' | 'withdrawBlockedReason'>,
  currentYear: string | null | undefined,
): boolean {
  if (currentYear) return row.academicYear === currentYear
  return row.withdrawBlockedReason !== 'year'
}

/** The student's row for a module, if any. */
export function findRow(
  rows: readonly MyEnrolment[] | undefined,
  code: string,
): MyEnrolment | undefined {
  return rows?.find((row) => row.moduleCode === code)
}

/** Credits of this year's active enrolments in `semester`. */
export function creditsUsed(
  rows: readonly MyEnrolment[] | undefined,
  semester: Semester,
  currentYear: string | null | undefined,
): number {
  if (!rows) return 0
  return rows
    .filter(
      (row) =>
        row.status === 'active' && row.semester === semester && isCurrentYearRow(row, currentYear),
    )
    .reduce((sum, row) => sum + row.credits, 0)
}

/** The codes of this year's active enrolments ("Enrolled only"). */
export function enrolledThisYear(
  rows: readonly MyEnrolment[] | undefined,
  currentYear: string | null | undefined,
): Set<string> {
  return new Set(
    (rows ?? [])
      .filter((row) => row.status === 'active' && isCurrentYearRow(row, currentYear))
      .map((row) => row.moduleCode),
  )
}

export interface EnrolStateInput {
  module: EnrolStateModule
  row: MyEnrolment | undefined
  currentYear: string | null | undefined
  /** This year's active credits in the module's semester. */
  creditsUsed: number
  /** `['student','enrolments']` was fetched less than 15 s ago. */
  enrolmentsFresh: boolean
}

const state = (kind: EnrolStateKind, disabled = true, again = false): EnrolState => ({
  kind,
  disabled,
  again,
})

export function deriveEnrolState({
  module,
  row,
  currentYear,
  creditsUsed: used,
  enrolmentsFresh,
}: EnrolStateInput): EnrolState {
  if (row?.status === 'active') {
    if (!isCurrentYearRow(row, currentYear)) return state('completed')
    if (row.canWithdraw) return state('enrolled', false)
    if (row.withdrawBlockedReason === 'results') return state('enrolledResults')
    return state('enrolledDeadline')
  }

  if (!module.isActive) return state('inactive')
  if (module.placesRemaining <= 0) return state('full')
  if (module.enrolmentState === 'notYetOpen') return state('notYetOpen')
  if (module.enrolmentState === 'closed') return state('closed')
  if (module.enrolmentState === 'noWindow') return state('noWindow')

  const again = row?.status === 'withdrawn'
  if (used + module.credits > CREDIT_LIMIT) return state('overCredits', enrolmentsFresh, again)
  return state(again ? 'enrolAgain' : 'enrol', false, again)
}
