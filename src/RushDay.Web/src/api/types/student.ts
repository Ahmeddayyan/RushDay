import type {
  AnnouncementView,
  GradeOutcome,
  GradeResult,
  MyEnrolment,
  PublicationBrief,
  Semester,
  TimetableEntry,
  WindowInfo,
} from './common'

/**
 * Request and response shapes of the student routes (02-api.md section 8.3, group `/api/me`),
 * mirrored exactly. Shared shapes (`MyEnrolment`, `TimetableEntry`, `GradeResult`, ...) live in
 * `common.ts`.
 */

/** Why `canWithdraw` is false: the first failing condition, in this order (02-api.md section 7). */
export type WithdrawBlockedReason = NonNullable<MyEnrolment['withdrawBlockedReason']>

/** `DashboardResponse.modules[]`: an active enrolment of the current academic year. */
export interface DashboardModule {
  code: string
  title: string
  credits: number
  semester: Semester
  academicYear: string
  enrolledAt: string
  canWithdraw: boolean
  withdrawBlockedReason: WithdrawBlockedReason | null
  withdrawalDeadlineAt: string | null
}

/**
 * `DashboardResponse.completed[]`: an active enrolment of an earlier academic year. `outcome`,
 * `mark` and `band` are set only when the grade is visible to the student.
 */
export interface CompletedModule {
  code: string
  title: string
  credits: number
  semester: Semester
  academicYear: string
  outcome: GradeOutcome | null
  mark: number | null
  /** `Classification.Band(mark)`, for example "Upper Second (2:1)". */
  band: string | null
}

/** Credits of the current year's active enrolments per semester. */
export interface CreditBudgets {
  autumn: number
  spring: number
  limit: 60
}

/** `GET /api/me/dashboard` */
export interface DashboardResponse {
  studentNumber: string
  fullName: string
  programme: string
  yearOfStudy: number
  academicYear: string
  currentSemester: Semester
  /** Active enrolments of the current year. */
  modules: DashboardModule[]
  /** Active enrolments of earlier years. */
  completed: CompletedModule[]
  /** The whole week: current-year modules of the current semester; the SPA derives "today". */
  timetable: TimetableEntry[]
  /** Visible grades only, every year. */
  results: GradeResult[]
  weightedAverage: number | null
  classification: string | null
  credits: CreditBudgets
  /** Earliest scheduled publication in the future. */
  nextPublication: PublicationBrief | null
  /** Most recent live publication. */
  latestPublication: PublicationBrief | null
  /** Current academic year. */
  enrolmentWindows: WindowInfo[]
  /** Latest 5 visible, pinned first. */
  announcements: AnnouncementView[]
}

export type ResultsSemesterState = 'published' | 'scheduled' | 'pending'

/**
 * One (academic year, semester) group of `GET /api/me/results`: `published` when at least one grade
 * is visible; `scheduled` when grades are published for a future instant (`publishAt`, the marks
 * themselves are never sent); `pending` when the student is enrolled and nothing is published.
 */
export interface ResultsSemester {
  academicYear: string
  semester: Semester
  state: ResultsSemesterState
  publishAt: string | null
  results: GradeResult[]
}

/** `GET /api/me/results`: newest year first, autumn before spring. */
export interface ResultsResponse {
  semesters: ResultsSemester[]
  weightedAverage: number | null
  classification: string | null
}

/** `GET /api/me/timetable` */
export type TimetableResponse = TimetableEntry[]

/** `GET /api/me/enrolments`: every academic year, current year first, then `enrolledAt` descending. */
export type MyEnrolmentsResponse = MyEnrolment[]

/** `POST /api/me/enrolments` body. */
export interface EnrolRequest {
  moduleCode: string
}

/** `POST /api/me/enrolments` 201 body (no `Location` header). */
export interface EnrolResponse {
  moduleCode: string
  enrolledAt: string
  placesRemaining: number
}
