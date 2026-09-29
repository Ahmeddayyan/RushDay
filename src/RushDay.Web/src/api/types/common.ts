/**
 * Every shared shape of 02-api.md section 7, mirrored exactly. Area files (`student.ts`,
 * `lecturer.ts`, `admin.ts`, ...) hold only their own routes' request and response shapes, so stages
 * S7-S10 never import one another's files (05-frontend.md section 3).
 */

export type Role = 'Student' | 'Lecturer' | 'Admin'
export type Semester = 'autumn' | 'spring'
export type Weekday =
  'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday'
export type GradeOutcome = 'mark' | 'absent' | 'deferred'

export interface Me {
  id: string
  username: string
  displayName: string
  role: Role
  studentNumber: string | null
  staffNumber: string | null
  mustChangePassword: boolean
  mfaEnabled: boolean
  mfaSetupRequired: boolean
  isDemo: boolean
  csrfToken: string
}

/** `POST /api/auth/login` when the account has a second factor: no claims are issued yet. */
export interface MfaChallenge {
  mfaRequired: true
  csrfToken: string
}

/** Discriminated by the presence of `mfaRequired`. */
export type LoginResponse = Me | MfaChallenge

export interface TimetableEntry {
  moduleCode: string
  moduleTitle: string
  semester: Semester
  day: Weekday
  /** "09:00" */
  startTime: string
  endTime: string
  room: string
  /** 'lab' when the room contains "-Lab". */
  kind: 'lecture' | 'lab'
}

export interface WindowInfo {
  id: string
  academicYear: string
  semester: Semester
  opensAt: string
  closesAt: string
  withdrawalDeadlineAt: string
  state: 'notYetOpen' | 'open' | 'closed'
}

export interface PublicationBrief {
  academicYear: string
  semester: Semester
  publishAt: string
  state: 'scheduled' | 'live'
}

export interface PublicationInfo extends PublicationBrief {
  id: string
  gradeCount: number
  moduleCount: number
  createdAt: string
  createdBy: string | null
  note: string | null
}

export interface Lecturer {
  staffNumber: string
  fullName: string
  title: string
  role: 'leader' | 'teacher'
  left: boolean
}

export interface ModuleSummary {
  code: string
  title: string
  department: string
  level: 1 | 2 | 3
  credits: number
  semester: Semester
  capacity: number
  /** Current academic year (modules.enrolled_count). */
  enrolledCount: number
  /** max(0, capacity - enrolledCount) */
  placesRemaining: number
  isActive: boolean
  lecturers: Lecturer[]
  enrolmentState: 'notYetOpen' | 'open' | 'closed' | 'noWindow'
  windowOpensAt: string | null
  windowClosesAt: string | null
  withdrawalDeadlineAt: string | null
}

export interface ModuleDetail extends ModuleSummary {
  description: string | null
  timetable: TimetableEntry[]
}

export interface GradeResult {
  moduleCode: string
  moduleTitle: string
  credits: number
  semester: Semester
  academicYear: string
  outcome: GradeOutcome
  /** Null iff outcome is not 'mark'. */
  mark: number | null
  publishedAt: string
  /** Set when an administrator corrected it after submission. */
  correctedAt: string | null
}

export interface AnnouncementView {
  id: string
  scope: 'university' | 'module'
  moduleCode: string | null
  title: string
  body: string
  pinned: boolean
  publishedAt: string
  expiresAt: string | null
  author: string
  createdAt: string
  updatedAt: string
}

export type MarksStatusValue = 'noStudents' | 'draft' | 'submitted' | 'scheduled' | 'published'

export interface MarksStatus {
  status: MarksStatusValue
  entered: number
  missing: number
  total: number
  submittedAt: string | null
  /** The publication's publish_at for scheduled and published. */
  publishedAt: string | null
}

export interface AuditEventView {
  id: string
  occurredAt: string
  actorUsername: string | null
  actorRole: string | null
  action: string
  subjectType: string
  subjectId: string | null
  studentNumber: string | null
  moduleCode: string | null
  details: Record<string, unknown> | null
  requestId: string | null
}

export interface AccountView {
  id: string
  username: string
  displayName: string
  role: Role
  studentNumber: string | null
  staffNumber: string | null
  email: string | null
  state: 'active' | 'locked' | 'disabled'
  lockoutEnd: string | null
  mustChangePassword: boolean
  mfaEnabled: boolean
  isDemo: boolean
  createdAt: string
  lastLoginAt: string | null
}

export interface MyEnrolment {
  moduleCode: string
  title: string
  credits: number
  semester: Semester
  academicYear: string
  status: 'active' | 'withdrawn'
  enrolledAt: string
  withdrawnAt: string | null
  canWithdraw: boolean
  withdrawBlockedReason: 'deadline' | 'results' | 'year' | null
  withdrawalDeadlineAt: string | null
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}
