import { apiFetch, type BlobResult } from '../client'
import type {
  AccountsQuery,
  AdminLecturer,
  AdminMarksSheet,
  AdminModule,
  AdminOverview,
  AdminResults,
  AdminResultsQuery,
  AdminRoster,
  AdminSettings,
  AdminStudentRow,
  AdminStudentsQuery,
  AdminStudentView,
  AnnouncementRequest,
  AuditQuery,
  CorrectMarkRequest,
  CorrectMarkResponse,
  CreateLecturerRequest,
  CreateModuleRequest,
  CreateStudentRequest,
  CreateWindowRequest,
  ModuleListQuery,
  OverrideEnrolRequest,
  OverrideEnrolResponse,
  ProvisionAccountRequest,
  ProvisionAccountResponse,
  PublicationReverted,
  PublishRequest,
  PublishResponse,
  ReasonRequest,
  ReschedulePublicationRequest,
  ResetPasswordRequest,
  ResetPasswordResponse,
  ReturnToDraftRequest,
  ReturnToDraftResponse,
  SetLecturersRequest,
  StudentLeftResponse,
  TrimResponse,
  UpdateLecturerRequest,
  UpdateModuleRequest,
  UpdateSettingsRequest,
  UpdateStudentRequest,
  UpdateWindowRequest,
} from '../types/admin'
import type {
  AccountView,
  AnnouncementView,
  AuditEventView,
  ModuleDetail,
  Paged,
  PublicationInfo,
  WindowInfo,
} from '../types/common'

/**
 * The administrator routes (02-api.md section 8.5), one function per route. React Query hooks live
 * in `features/admin/hooks`; these stay plain so tests and downloads can call them directly.
 */

const ADMIN = '/api/admin'

type QueryValue = string | number | boolean | null | undefined

/** `?a=1&b=x`, skipping empty values; '' when nothing is set. */
export function toQueryString(params: Readonly<Record<string, QueryValue>>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue
    search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

const seg = encodeURIComponent

function post<T>(path: string, body?: unknown): Promise<T> {
  return apiFetch<T>(path, body === undefined ? { method: 'POST' } : { method: 'POST', body })
}

function put<T>(path: string, body: unknown): Promise<T> {
  return apiFetch<T>(path, { method: 'PUT', body })
}

function del<T = void>(path: string): Promise<T> {
  return apiFetch<T>(path, { method: 'DELETE' })
}

// -------------------------------------------------------------------------------------------------
// Overview and settings

export function getOverview(): Promise<AdminOverview> {
  return apiFetch<AdminOverview>(`${ADMIN}/overview`)
}

export function getSettings(): Promise<AdminSettings> {
  return apiFetch<AdminSettings>(`${ADMIN}/settings`)
}

export function updateSettings(body: UpdateSettingsRequest): Promise<AdminSettings> {
  return put<AdminSettings>(`${ADMIN}/settings`, body)
}

// -------------------------------------------------------------------------------------------------
// Enrolment windows

export function getWindows(): Promise<WindowInfo[]> {
  return apiFetch<WindowInfo[]>(`${ADMIN}/enrolment-windows`)
}

export function createWindow(body: CreateWindowRequest): Promise<WindowInfo> {
  return post<WindowInfo>(`${ADMIN}/enrolment-windows`, body)
}

export function updateWindow(id: string, body: UpdateWindowRequest): Promise<WindowInfo> {
  return put<WindowInfo>(`${ADMIN}/enrolment-windows/${seg(id)}`, body)
}

export function deleteWindow(id: string): Promise<void> {
  return del(`${ADMIN}/enrolment-windows/${seg(id)}`)
}

// -------------------------------------------------------------------------------------------------
// Results

export function getResults({ semester, academicYear }: AdminResultsQuery): Promise<AdminResults> {
  return apiFetch<AdminResults>(`${ADMIN}/results${toQueryString({ semester, academicYear })}`)
}

export function publishResults(body: PublishRequest): Promise<PublishResponse> {
  return post<PublishResponse>(`${ADMIN}/results/publish`, body)
}

export function reschedulePublication(
  id: string,
  body: ReschedulePublicationRequest,
): Promise<PublicationInfo> {
  return put<PublicationInfo>(`${ADMIN}/results/publications/${seg(id)}`, body)
}

/** Cancels a scheduled publication: its marks go back to Submitted. */
export function cancelPublication(id: string): Promise<PublicationReverted> {
  return del<PublicationReverted>(`${ADMIN}/results/publications/${seg(id)}`)
}

export function unpublishPublication(
  id: string,
  body: ReasonRequest,
): Promise<PublicationReverted> {
  return post<PublicationReverted>(`${ADMIN}/results/publications/${seg(id)}/unpublish`, body)
}

export function returnModuleToDraft(
  code: string,
  body: ReturnToDraftRequest,
): Promise<ReturnToDraftResponse> {
  return post<ReturnToDraftResponse>(`${ADMIN}/results/modules/${seg(code)}/return-to-draft`, body)
}

export function correctMark(
  code: string,
  studentNumber: string,
  body: CorrectMarkRequest,
): Promise<CorrectMarkResponse> {
  return post<CorrectMarkResponse>(
    `${ADMIN}/results/modules/${seg(code)}/marks/${seg(studentNumber)}/correct`,
    body,
  )
}

// -------------------------------------------------------------------------------------------------
// Students

export function getStudents(query: AdminStudentsQuery = {}): Promise<Paged<AdminStudentRow>> {
  return apiFetch<Paged<AdminStudentRow>>(
    `${ADMIN}/students${toQueryString({
      q: query.q?.trim(),
      accountState: query.accountState,
      page: query.page,
      pageSize: query.pageSize,
    })}`,
  )
}

export function createStudent(body: CreateStudentRequest): Promise<AdminStudentRow> {
  return post<AdminStudentRow>(`${ADMIN}/students`, body)
}

/** Audited on the server as `student.viewed`. */
export function getStudent(studentNumber: string): Promise<AdminStudentView> {
  return apiFetch<AdminStudentView>(`${ADMIN}/students/${seg(studentNumber)}`)
}

export function updateStudent(
  studentNumber: string,
  body: UpdateStudentRequest,
): Promise<AdminStudentRow> {
  return put<AdminStudentRow>(`${ADMIN}/students/${seg(studentNumber)}`, body)
}

export function markStudentLeft(
  studentNumber: string,
  body: ReasonRequest,
): Promise<StudentLeftResponse> {
  return post<StudentLeftResponse>(`${ADMIN}/students/${seg(studentNumber)}/leave`, body)
}

/** The personal-data export's path, for `lib/download.ts` (audited as `student.exported`). */
export function studentExportPath(studentNumber: string): string {
  return `${ADMIN}/students/${seg(studentNumber)}/export.json`
}

export function overrideEnrol(
  studentNumber: string,
  body: OverrideEnrolRequest,
): Promise<OverrideEnrolResponse> {
  return post<OverrideEnrolResponse>(`${ADMIN}/students/${seg(studentNumber)}/enrolments`, body)
}

export function overrideWithdraw(
  studentNumber: string,
  code: string,
  body: ReasonRequest,
): Promise<void> {
  return apiFetch<void>(
    `${ADMIN}/students/${seg(studentNumber)}/enrolments/${seg(code)}/withdraw`,
    { method: 'POST', body, expect: 'void' },
  )
}

// -------------------------------------------------------------------------------------------------
// Modules and lecturers

export function getAdminModules(includeInactive = false): Promise<AdminModule[]> {
  return apiFetch<AdminModule[]>(
    `${ADMIN}/modules${toQueryString({ includeInactive: includeInactive || undefined })}`,
  )
}

/** `GET /api/modules/{code}` (any role): live counts, description and the timetable slots. */
export function getModuleDetail(code: string): Promise<ModuleDetail> {
  return apiFetch<ModuleDetail>(`/api/modules/${seg(code)}`)
}

export function createModule(body: CreateModuleRequest): Promise<ModuleDetail> {
  return post<ModuleDetail>(`${ADMIN}/modules`, body)
}

export function updateModule(code: string, body: UpdateModuleRequest): Promise<ModuleDetail> {
  return put<ModuleDetail>(`${ADMIN}/modules/${seg(code)}`, body)
}

function moduleListQuery(query: ModuleListQuery): string {
  return toQueryString({
    q: query.q?.trim(),
    page: query.page,
    pageSize: query.pageSize,
    academicYear: query.academicYear,
  })
}

export function getModuleRoster(code: string, query: ModuleListQuery = {}): Promise<AdminRoster> {
  return apiFetch<AdminRoster>(`${ADMIN}/modules/${seg(code)}/roster${moduleListQuery(query)}`)
}

export function getModuleMarks(
  code: string,
  query: ModuleListQuery = {},
): Promise<AdminMarksSheet> {
  return apiFetch<AdminMarksSheet>(`${ADMIN}/modules/${seg(code)}/marks${moduleListQuery(query)}`)
}

export function trimModule(code: string, body: ReasonRequest): Promise<TrimResponse> {
  return post<TrimResponse>(`${ADMIN}/modules/${seg(code)}/trim-to-capacity`, body)
}

export function setModuleLecturers(code: string, body: SetLecturersRequest): Promise<ModuleDetail> {
  return put<ModuleDetail>(`${ADMIN}/modules/${seg(code)}/lecturers`, body)
}

export function getLecturers(q = ''): Promise<AdminLecturer[]> {
  return apiFetch<AdminLecturer[]>(`${ADMIN}/lecturers${toQueryString({ q: q.trim() })}`)
}

export function createLecturer(body: CreateLecturerRequest): Promise<AdminLecturer> {
  return post<AdminLecturer>(`${ADMIN}/lecturers`, body)
}

export function updateLecturer(
  staffNumber: string,
  body: UpdateLecturerRequest,
): Promise<AdminLecturer> {
  return put<AdminLecturer>(`${ADMIN}/lecturers/${seg(staffNumber)}`, body)
}

export function markLecturerLeft(staffNumber: string, body: ReasonRequest): Promise<AdminLecturer> {
  return post<AdminLecturer>(`${ADMIN}/lecturers/${seg(staffNumber)}/leave`, body)
}

// -------------------------------------------------------------------------------------------------
// Accounts

export function getAccounts(query: AccountsQuery = {}): Promise<Paged<AccountView>> {
  return apiFetch<Paged<AccountView>>(
    `${ADMIN}/accounts${toQueryString({
      q: query.q?.trim(),
      role: query.role,
      state: query.state,
      page: query.page,
      pageSize: query.pageSize,
    })}`,
  )
}

export function provisionAccount(body: ProvisionAccountRequest): Promise<ProvisionAccountResponse> {
  return post<ProvisionAccountResponse>(`${ADMIN}/accounts`, body)
}

export type AccountAction = 'lock' | 'unlock' | 'disable' | 'enable' | 'reset-mfa'

/** Lock, unlock, disable, enable or reset the second factor: each answers the updated account. */
export function accountAction(id: string, action: AccountAction): Promise<AccountView> {
  return post<AccountView>(`${ADMIN}/accounts/${seg(id)}/${action}`)
}

export function resetAccountPassword(
  id: string,
  body: ResetPasswordRequest = {},
): Promise<ResetPasswordResponse> {
  return post<ResetPasswordResponse>(`${ADMIN}/accounts/${seg(id)}/reset-password`, body)
}

// -------------------------------------------------------------------------------------------------
// Announcements (university scope; administrators may edit or delete any scope)

export function getAdminAnnouncements(): Promise<AnnouncementView[]> {
  return apiFetch<AnnouncementView[]>(`${ADMIN}/announcements`)
}

export function createAnnouncement(body: AnnouncementRequest): Promise<AnnouncementView> {
  return post<AnnouncementView>(`${ADMIN}/announcements`, body)
}

export function updateAnnouncement(
  id: string,
  body: AnnouncementRequest,
): Promise<AnnouncementView> {
  return put<AnnouncementView>(`${ADMIN}/announcements/${seg(id)}`, body)
}

export function deleteAnnouncement(id: string): Promise<void> {
  return del(`${ADMIN}/announcements/${seg(id)}`)
}

// -------------------------------------------------------------------------------------------------
// Audit

function auditFilters(query: AuditQuery): Record<string, QueryValue> {
  return {
    actor: query.actor?.trim(),
    studentNumber: query.studentNumber?.trim(),
    moduleCode: query.moduleCode?.trim(),
    action: query.action,
    from: query.from,
    to: query.to,
  }
}

export function getAudit(query: AuditQuery = {}): Promise<Paged<AuditEventView>> {
  return apiFetch<Paged<AuditEventView>>(
    `${ADMIN}/audit${toQueryString({
      ...auditFilters(query),
      page: query.page,
      pageSize: query.pageSize,
    })}`,
  )
}

/** The CSV export with the same filters (no paging). The server audits it before streaming. */
export function exportAudit(query: AuditQuery = {}): Promise<BlobResult> {
  return apiFetch<BlobResult>(`${ADMIN}/audit/export.csv${toQueryString(auditFilters(query))}`, {
    expect: 'blob',
  })
}
