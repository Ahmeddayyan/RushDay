import type {
  AccountView,
  AuditEventView,
  GradeOutcome,
  MarksStatus,
  MarksStatusValue,
  ModuleSummary,
  PublicationInfo,
  Role,
  Semester,
  WindowInfo,
} from './common'

/**
 * Request and response shapes of the administrator routes (02-api.md section 8.5), mirrored field
 * for field. Shared shapes (`AccountView`, `AuditEventView`, `MarksStatus`, `PublicationInfo`,
 * `WindowInfo`, `ModuleSummary`, `Paged<T>`) live in `common.ts`. Instants are ISO-8601 UTC strings
 * with exactly three fractional digits (`2026-09-28T09:00:00.000Z`).
 */

// -------------------------------------------------------------------------------------------------
// Overview and settings

export interface AdminOverviewCounts {
  students: number
  lecturers: number
  modules: number
  /** Current academic year. */
  activeEnrolments: number
  accounts: number
  lockedAccounts: number
  disabledAccounts: number
}

/** One row per semester of the current academic year; `modulesTotal` excludes `noStudents`. */
export interface SubmissionProgress {
  semester: Semester
  modulesTotal: number
  noStudents: number
  draft: number
  submitted: number
  scheduled: number
  published: number
}

/** `GET /api/admin/overview` */
export interface AdminOverview {
  counts: AdminOverviewCounts
  academicYear: string
  enrolmentWindows: WindowInfo[]
  nextPublication: PublicationInfo | null
  latestPublication: PublicationInfo | null
  submissionProgress: SubmissionProgress[]
  /** The ten most recent audit rows. */
  recentAudit: AuditEventView[]
  database: 'ok' | 'degraded'
}

/** `GET /api/admin/settings` and the answer of `PUT`. */
export interface AdminSettings {
  academicYear: string
  currentSemester: Semester
  institutionName: string
  institutionShortName: string
  timeZone: string
  supportEmail: string | null
  supportUrl: string | null
  updatedAt: string
}

/** `PUT /api/admin/settings`: `academicYear` matches `^\d{4}/\d{2}$`. */
export interface UpdateSettingsRequest {
  academicYear: string
  currentSemester: Semester
  /** 1..200 */
  institutionName: string
  /** 1..32 */
  institutionShortName: string
  timeZone: string
  /** Email, at most 256 characters; null clears it. */
  supportEmail?: string | null
  /** Absolute https URL, at most 400 characters; null clears it. */
  supportUrl?: string | null
}

// -------------------------------------------------------------------------------------------------
// Enrolment windows

/** `POST /api/admin/enrolment-windows` */
export interface CreateWindowRequest {
  academicYear: string
  semester: Semester
  opensAt: string
  closesAt: string
  withdrawalDeadlineAt: string
}

/** `PUT /api/admin/enrolment-windows/{id}` */
export interface UpdateWindowRequest {
  opensAt: string
  closesAt: string
  withdrawalDeadlineAt: string
}

// -------------------------------------------------------------------------------------------------
// Results

export interface AdminResultsModule {
  code: string
  title: string
  leader: string | null
  /** Equals `marks.total`: active enrolments of that year. */
  enrolledCount: number
  marks: MarksStatus
}

/** `GET /api/admin/results?semester=&academicYear=` */
export interface AdminResults {
  academicYear: string
  semester: Semester
  /** Sorted by `marks.status` (`noStudents` last), then code. */
  modules: AdminResultsModule[]
  publications: PublicationInfo[]
}

export interface AdminResultsQuery {
  semester: Semester
  /** Defaults to the settings year on the server. */
  academicYear?: string
}

/** `POST /api/admin/results/publish` */
export interface PublishRequest {
  academicYear: string
  semester: Semester
  /** An instant earlier than now is replaced by now on the server. */
  publishAt: string
  announce: boolean
  /** At most 400 characters. */
  note?: string
}

export interface PublishExclusion {
  code: string
  status: 'draft' | 'submitted'
  reason: 'notSubmitted' | 'marksMissing'
  marksMissing: number
}

export interface PublishResponse {
  publication: PublicationInfo
  published: { modules: number; grades: number }
  excluded: PublishExclusion[]
}

/** `PUT /api/admin/results/publications/{id}` */
export interface ReschedulePublicationRequest {
  publishAt: string
}

/** The answer of cancel (`DELETE`) and unpublish. */
export interface PublicationReverted {
  academicYear: string
  semester: Semester
  grades: number
}

/** `POST /api/admin/results/publications/{id}/unpublish` */
export interface UnpublishRequest {
  /** 10..400 */
  reason: string
}

/** `POST /api/admin/results/modules/{code}/return-to-draft` */
export interface ReturnToDraftRequest {
  /** 10..400 */
  reason: string
  academicYear?: string
}

export interface ReturnToDraftResponse {
  code: string
  status: 'draft'
  fromScheduledPublication: boolean
}

/** `POST /api/admin/results/modules/{code}/marks/{studentNumber}/correct` */
export interface CorrectMarkRequest {
  /** Defaults to `mark` on the server. */
  outcome?: GradeOutcome
  /** 0..100 integer when `outcome = mark`, else null. */
  mark: number | null
  /** 10..400 */
  reason: string
}

export interface MarkValue {
  mark: number | null
  outcome: GradeOutcome
}

export interface CorrectMarkResponse {
  studentNumber: string
  before: MarkValue
  after: MarkValue
  version: number
  correctedAt: string
}

// -------------------------------------------------------------------------------------------------
// Students

export type StudentAccountState = 'none' | 'active' | 'locked' | 'disabled'

/** A row of `GET /api/admin/students`, and the answer of create and update. */
export interface AdminStudentRow {
  studentNumber: string
  fullName: string
  programme: string
  yearOfStudy: number
  email: string | null
  leftAt: string | null
  accountState: StudentAccountState
}

export interface AdminStudentsQuery {
  /** Student number prefix or any part of the name, at most 100 characters. */
  q?: string
  accountState?: StudentAccountState
  page?: number
  pageSize?: number
}

/** `POST /api/admin/students` */
export interface CreateStudentRequest {
  /** `^S\d{6}$` */
  studentNumber: string
  /** 1..200 */
  fullName: string
  /** 1..200 */
  programme: string
  /** 1..6 */
  yearOfStudy: number
  email?: string | null
}

/** `PUT /api/admin/students/{studentNumber}` */
export interface UpdateStudentRequest {
  fullName: string
  programme: string
  yearOfStudy: number
  email: string | null
}

/** Body of every "leave", "trim", "unpublish" style action that needs a reason (10..400). */
export interface ReasonRequest {
  reason: string
}

/** `POST /api/admin/students/{studentNumber}/leave` */
export interface StudentLeftResponse {
  studentNumber: string
  leftAt: string
  withdrawn: number
}

export interface AdminStudentRecord {
  studentNumber: string
  fullName: string
  programme: string
  yearOfStudy: number
  email: string | null
  leftAt: string | null
}

export interface AdminStudentEnrolment {
  moduleCode: string
  title: string
  credits: number
  semester: Semester
  academicYear: string
  status: 'active' | 'withdrawn'
  source: 'seed' | 'self' | 'admin'
  enrolledAt: string
  withdrawnAt: string | null
}

export interface AdminStudentGrade {
  moduleCode: string
  moduleTitle: string
  credits: number
  semester: Semester
  academicYear: string
  outcome: GradeOutcome
  mark: number | null
  status: 'draft' | 'submitted' | 'published'
  publishedAt: string | null
  /** The student-visibility predicate: Published, `published_at <= now`, active enrolment. */
  visibleToStudent: boolean
  version: number
  correctedAt: string | null
}

/** `GET /api/admin/students/{studentNumber}` (audited as `student.viewed`). */
export interface AdminStudentView {
  student: AdminStudentRecord
  account: AccountView | null
  enrolments: AdminStudentEnrolment[]
  grades: AdminStudentGrade[]
  /** Visible grades only, as the student sees it. */
  weightedAverage: number | null
  classification: string | null
  /** The ten most recent audit rows about this student. */
  recentAudit: AuditEventView[]
}

/** `GET /api/admin/students/{studentNumber}/export.json`: `AdminStudentView` minus the audit and account. */
export interface AdminStudentExport {
  student: AdminStudentRecord
  enrolments: AdminStudentEnrolment[]
  grades: AdminStudentGrade[]
  weightedAverage: number | null
  classification: string | null
  exportedAt: string
}

/** `POST /api/admin/students/{studentNumber}/enrolments` */
export interface OverrideEnrolRequest {
  moduleCode: string
  /** 10..400 */
  reason: string
  /** Raises capacity by one only when the module is full. */
  forceCapacity?: boolean
}

export interface OverrideEnrolResponse {
  moduleCode: string
  enrolledAt: string
  placesRemaining: number
  capacityRaised: boolean
}

// -------------------------------------------------------------------------------------------------
// Modules and lecturers

/** A row of `GET /api/admin/modules`. */
export interface AdminModule extends ModuleSummary {
  description: string | null
  marks: MarksStatus
}

/** `POST /api/admin/modules` */
export interface CreateModuleRequest {
  /** `^[A-Z]{2}\d{4}$` */
  code: string
  /** 1..200 */
  title: string
  /** At most 2,000 characters. */
  description?: string | null
  /** 5..60 */
  credits: number
  /** 0..10,000 */
  capacity: number
  semester: Semester
}

/** `PUT /api/admin/modules/{code}` */
export interface UpdateModuleRequest {
  title: string
  description: string | null
  credits: number
  capacity: number
  semester: Semester
  isActive: boolean
}

export interface RosterEntry {
  studentNumber: string
  fullName: string
  programme: string
  yearOfStudy: number
  status: 'active' | 'withdrawn'
  enrolledAt: string
  withdrawnAt: string | null
}

/** `GET /api/admin/modules/{code}/roster`: the lecturer roster shape of section 8.4. */
export interface AdminRoster {
  module: ModuleSummary
  items: RosterEntry[]
  page: number
  pageSize: number
  total: number
}

export interface ModuleListQuery {
  q?: string
  page?: number
  pageSize?: number
  /** Defaults to the settings year on the server. */
  academicYear?: string
}

export interface AdminMarksRow {
  studentNumber: string
  fullName: string
  enrolmentStatus: 'active' | 'withdrawn'
  outcome: GradeOutcome | null
  mark: number | null
  gradeStatus: 'draft' | 'submitted' | 'published' | null
  version: number | null
  updatedAt: string | null
  enteredBy: string | null
  correctedAt: string | null
}

/** `GET /api/admin/modules/{code}/marks`: `MarksSheet` of section 8.4 with `myRole: null`. */
export interface AdminMarksSheet {
  code: string
  title: string
  academicYear: string
  status: MarksStatusValue
  submittedAt: string | null
  publishedAt: string | null
  myRole: 'leader' | 'teacher' | null
  leader: string | null
  summary: { entered: number; missing: number; total: number }
  rows: AdminMarksRow[]
  page: number
  pageSize: number
  total: number
}

/** `POST /api/admin/modules/{code}/trim-to-capacity` */
export interface TrimResponse {
  code: string
  capacity: number
  before: number
  after: number
  withdrawn: string[]
}

export interface LecturerAssignment {
  staffNumber: string
  role: 'leader' | 'teacher'
}

/** `PUT /api/admin/modules/{code}/lecturers`: exactly one leader, no duplicates, nobody who has left. */
export interface SetLecturersRequest {
  assignments: LecturerAssignment[]
}

export type LecturerTitle = 'Dr' | 'Prof' | 'Mr' | 'Ms' | 'Mx'

/** A row of `GET /api/admin/lecturers`, and the answer of create, update and leave. */
export interface AdminLecturer {
  staffNumber: string
  fullName: string
  title: string
  department: string
  email: string | null
  leftAt: string | null
  hasAccount: boolean
  moduleCodes: string[]
}

/** `POST /api/admin/lecturers` */
export interface CreateLecturerRequest {
  /** `^L\d{5}$` */
  staffNumber: string
  fullName: string
  title: LecturerTitle
  /** 1..8 */
  department: string
  email?: string | null
}

/** `PUT /api/admin/lecturers/{staffNumber}` */
export interface UpdateLecturerRequest {
  fullName: string
  title: LecturerTitle
  department: string
  email: string | null
}

// -------------------------------------------------------------------------------------------------
// Accounts

export interface AccountsQuery {
  /** Username prefix or display-name fragment, at most 100 characters. */
  q?: string
  role?: Role
  state?: AccountView['state']
  page?: number
  pageSize?: number
}

/** `POST /api/admin/accounts` */
export interface ProvisionAccountRequest {
  /** 1..64, letters, digits, `.`, `_` and `-`. */
  username: string
  /** 1..200 */
  displayName: string
  role: Role
  studentNumber?: string | null
  staffNumber?: string | null
  email?: string | null
  /** Checked against the password policy; generated (16 characters) when omitted. */
  temporaryPassword?: string
}

/** Shown once. */
export interface ProvisionAccountResponse {
  account: AccountView
  temporaryPassword: string
}

/** `POST /api/admin/accounts/{id}/reset-password` */
export interface ResetPasswordRequest {
  temporaryPassword?: string
}

export interface ResetPasswordResponse {
  temporaryPassword: string
}

// -------------------------------------------------------------------------------------------------
// Announcements (university scope)

/** `POST /api/admin/announcements` and `PUT /api/admin/announcements/{id}`. */
export interface AnnouncementRequest {
  /** 1..120 */
  title: string
  /** 1..4,000 */
  body: string
  pinned?: boolean
  publishedAt?: string
  expiresAt?: string
}

// -------------------------------------------------------------------------------------------------
// Audit

export interface AuditQuery {
  /** Username, at most 100 characters. */
  actor?: string
  studentNumber?: string
  moduleCode?: string
  /** At most 100 characters. */
  action?: string
  from?: string
  to?: string
  page?: number
  /** At most 200. */
  pageSize?: number
}
