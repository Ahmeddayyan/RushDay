import type { GradeOutcome, MarksStatus, MarksStatusValue, ModuleSummary } from './common'

/**
 * Request and response shapes of the lecturer routes only (02-api.md section 8.4), mirrored exactly.
 * Shared shapes live in `./common`; stages S7, S9 and S10 never import this file
 * (05-frontend.md section 3).
 */

export interface LecturerModuleSummary extends ModuleSummary {
  myRole: 'leader' | 'teacher'
  marks: MarksStatus
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

export interface RosterResponse {
  module: ModuleSummary
  items: RosterEntry[]
  page: number
  pageSize: number
  total: number
}

export type GradeStatus = 'draft' | 'submitted' | 'published'

export interface MarksRow {
  studentNumber: string
  fullName: string
  enrolmentStatus: 'active' | 'withdrawn'
  outcome: GradeOutcome | null
  mark: number | null
  gradeStatus: GradeStatus | null
  version: number | null
  updatedAt: string | null
  enteredBy: string | null
  correctedAt: string | null
}

/** `rows` is the current page; `summary` always covers the whole module. */
export interface MarksSheet {
  code: string
  title: string
  academicYear: string
  status: MarksStatusValue
  submittedAt: string | null
  publishedAt: string | null
  myRole: 'leader' | 'teacher' | null
  leader: string | null
  summary: { entered: number; missing: number; total: number }
  rows: MarksRow[]
  page: number
  pageSize: number
  total: number
}

export interface SaveMarksRowRequest {
  studentNumber: string
  mark: number | null
  /** Defaults to 'mark' server-side. */
  outcome?: GradeOutcome
  version: number | null
}

export interface SaveMarksRequest {
  rows: SaveMarksRowRequest[]
}

export interface SaveMarksResponse {
  summary: MarksSheet['summary']
  rows: MarksRow[]
}

export interface SubmitMarksResponse {
  code: string
  status: 'submitted'
  submittedAt: string
  gradeCount: number
}

export interface ModuleAnnouncementRequest {
  title: string
  body: string
  pinned?: boolean
  publishedAt?: string
  expiresAt?: string
}
