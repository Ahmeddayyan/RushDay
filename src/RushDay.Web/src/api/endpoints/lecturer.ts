import { apiFetch } from '../client'
import type { AnnouncementView } from '../types/common'
import type {
  LecturerModuleSummary,
  MarksSheet,
  ModuleAnnouncementRequest,
  RosterResponse,
  SaveMarksRequest,
  SaveMarksResponse,
  SubmitMarksResponse,
} from '../types/lecturer'

/**
 * The `/api/lecturer` routes (02-api.md section 8.4). Query params are appended by hand (no request
 * library) so every call stays a one-line `apiFetch`.
 */

function query(params: Record<string, string | number | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

/** GET /api/lecturer/modules */
export function getMyModules(): Promise<LecturerModuleSummary[]> {
  return apiFetch<LecturerModuleSummary[]>('/api/lecturer/modules')
}

/** GET /api/lecturer/modules/{code}/roster */
export function getRoster(
  code: string,
  params: { q: string; page: number; pageSize: number },
): Promise<RosterResponse> {
  return apiFetch<RosterResponse>(`/api/lecturer/modules/${code}/roster${query(params)}`)
}

/** GET /api/lecturer/modules/{code}/roster.csv (Should) */
export function rosterCsvPath(code: string): string {
  return `/api/lecturer/modules/${code}/roster.csv`
}

/** GET /api/lecturer/modules/{code}/marks */
export function getMarks(
  code: string,
  params: { q: string; page: number; pageSize?: number },
): Promise<MarksSheet> {
  return apiFetch<MarksSheet>(`/api/lecturer/modules/${code}/marks${query(params)}`)
}

/** PUT /api/lecturer/modules/{code}/marks: at most 500 rows per call; the caller chunks. */
export function saveMarks(code: string, body: SaveMarksRequest): Promise<SaveMarksResponse> {
  return apiFetch<SaveMarksResponse>(`/api/lecturer/modules/${code}/marks`, {
    method: 'PUT',
    body,
  })
}

/** POST /api/lecturer/modules/{code}/marks/submit: leader only. */
export function submitMarks(code: string): Promise<SubmitMarksResponse> {
  return apiFetch<SubmitMarksResponse>(`/api/lecturer/modules/${code}/marks/submit`, {
    method: 'POST',
  })
}

/** GET /api/lecturer/modules/{code}/announcements */
export function getModuleAnnouncements(code: string): Promise<AnnouncementView[]> {
  return apiFetch<AnnouncementView[]>(`/api/lecturer/modules/${code}/announcements`)
}

/** POST /api/lecturer/modules/{code}/announcements */
export function createModuleAnnouncement(
  code: string,
  body: ModuleAnnouncementRequest,
): Promise<AnnouncementView> {
  return apiFetch<AnnouncementView>(`/api/lecturer/modules/${code}/announcements`, {
    method: 'POST',
    body,
  })
}

/** PUT /api/lecturer/modules/{code}/announcements/{id} */
export function updateModuleAnnouncement(
  code: string,
  id: string,
  body: ModuleAnnouncementRequest,
): Promise<AnnouncementView> {
  return apiFetch<AnnouncementView>(`/api/lecturer/modules/${code}/announcements/${id}`, {
    method: 'PUT',
    body,
  })
}

/** DELETE /api/lecturer/modules/{code}/announcements/{id} */
export function deleteModuleAnnouncement(code: string, id: string): Promise<void> {
  return apiFetch<void>(`/api/lecturer/modules/${code}/announcements/${id}`, {
    method: 'DELETE',
    expect: 'void',
  })
}
