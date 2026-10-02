import { http, HttpResponse } from 'msw'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'

import { configureClient } from '@/api/client'
import { server } from '@/test/server'

import * as admin from './admin'

/**
 * Every administrator route of 02-api.md section 8.5 with its method, path, query string and body:
 * the endpoint functions must mirror the route table exactly.
 */

interface Seen {
  method: string
  path: string
  search: string
  body: unknown
  csrf: string | null
}

let seen: Seen[] = []

beforeEach(() => {
  seen = []
  configureClient({
    getCsrfToken: () => 'token',
    refreshCsrfToken: () => Promise.resolve(),
    onUnauthenticated: () => {},
  })
  server.use(
    http.all('/api/*', async ({ request }) => {
      const url = new URL(request.url)
      const text = request.method === 'GET' ? '' : await request.text()
      seen.push({
        method: request.method,
        path: url.pathname,
        search: url.search,
        body: text ? (JSON.parse(text) as unknown) : undefined,
        csrf: request.headers.get('X-CSRF-TOKEN'),
      })
      if (url.pathname.endsWith('.csv')) {
        return new HttpResponse('a,b', {
          headers: { 'Content-Type': 'text/csv', 'X-RushDay-Truncated': 'true' },
        })
      }
      if (url.pathname.endsWith('/withdraw') || request.method === 'DELETE') {
        return new HttpResponse(null, { status: 204 })
      }
      return HttpResponse.json({ ok: true })
    }),
  )
})

afterEach(() => {
  configureClient({
    getCsrfToken: () => null,
    refreshCsrfToken: () => Promise.resolve(),
    onUnauthenticated: () => {},
  })
})

const last = () => seen.at(-1)!

describe('admin endpoints', () => {
  it('builds query strings without empty values', () => {
    expect(admin.toQueryString({ a: 1, b: '', c: null, d: undefined, e: 'x y' })).toBe('?a=1&e=x+y')
    expect(admin.toQueryString({})).toBe('')
  })

  it.each([
    ['getOverview', () => admin.getOverview(), 'GET', '/api/admin/overview', ''],
    ['getSettings', () => admin.getSettings(), 'GET', '/api/admin/settings', ''],
    ['getWindows', () => admin.getWindows(), 'GET', '/api/admin/enrolment-windows', ''],
    [
      'getResults',
      () => admin.getResults({ semester: 'spring', academicYear: '2026/27' }),
      'GET',
      '/api/admin/results',
      '?semester=spring&academicYear=2026%2F27',
    ],
    [
      'getStudents',
      () => admin.getStudents({ q: ' khan ', accountState: 'none', page: 2, pageSize: 25 }),
      'GET',
      '/api/admin/students',
      '?q=khan&accountState=none&page=2&pageSize=25',
    ],
    ['getStudent', () => admin.getStudent('S000002'), 'GET', '/api/admin/students/S000002', ''],
    ['getAdminModules', () => admin.getAdminModules(), 'GET', '/api/admin/modules', ''],
    [
      'getAdminModules inactive',
      () => admin.getAdminModules(true),
      'GET',
      '/api/admin/modules',
      '?includeInactive=true',
    ],
    ['getModuleDetail', () => admin.getModuleDetail('CS3099'), 'GET', '/api/modules/CS3099', ''],
    [
      'getModuleRoster',
      () => admin.getModuleRoster('CS3099', { q: 'S0001', page: 1, pageSize: 50 }),
      'GET',
      '/api/admin/modules/CS3099/roster',
      '?q=S0001&page=1&pageSize=50',
    ],
    [
      'getModuleMarks',
      () => admin.getModuleMarks('CS3099', { page: 3, pageSize: 100, academicYear: '2025/26' }),
      'GET',
      '/api/admin/modules/CS3099/marks',
      '?page=3&pageSize=100&academicYear=2025%2F26',
    ],
    ['getLecturers', () => admin.getLecturers(' L0 '), 'GET', '/api/admin/lecturers', '?q=L0'],
    [
      'getAccounts',
      () => admin.getAccounts({ q: 'adm', role: 'Admin', state: 'locked', page: 1, pageSize: 25 }),
      'GET',
      '/api/admin/accounts',
      '?q=adm&role=Admin&state=locked&page=1&pageSize=25',
    ],
    [
      'getAdminAnnouncements',
      () => admin.getAdminAnnouncements(),
      'GET',
      '/api/admin/announcements',
      '',
    ],
    [
      'getAudit',
      () =>
        admin.getAudit({
          actor: 'admin',
          action: 'grade.corrected',
          studentNumber: 'S000001',
          moduleCode: 'CS3099',
          from: '2026-09-01T00:00:00.000Z',
          to: '2026-09-30T00:00:00.000Z',
          page: 2,
          pageSize: 50,
        }),
      'GET',
      '/api/admin/audit',
      '?actor=admin&studentNumber=S000001&moduleCode=CS3099&action=grade.corrected&from=2026-09-01T00%3A00%3A00.000Z&to=2026-09-30T00%3A00%3A00.000Z&page=2&pageSize=50',
    ],
  ])('%s → %s %s', async (_name, call, method, path, search) => {
    await call()
    expect(last()).toMatchObject({ method, path, search })
  })

  it.each([
    [
      'updateSettings',
      () =>
        admin.updateSettings({
          academicYear: '2027/28',
          currentSemester: 'autumn',
          institutionName: 'N',
          institutionShortName: 'N',
          timeZone: 'Europe/London',
        }),
      'PUT',
      '/api/admin/settings',
    ],
    [
      'createWindow',
      () =>
        admin.createWindow({
          academicYear: '2027/28',
          semester: 'autumn',
          opensAt: 'a',
          closesAt: 'b',
          withdrawalDeadlineAt: 'c',
        }),
      'POST',
      '/api/admin/enrolment-windows',
    ],
    [
      'updateWindow',
      () => admin.updateWindow('w1', { opensAt: 'a', closesAt: 'b', withdrawalDeadlineAt: 'c' }),
      'PUT',
      '/api/admin/enrolment-windows/w1',
    ],
    ['deleteWindow', () => admin.deleteWindow('w1'), 'DELETE', '/api/admin/enrolment-windows/w1'],
    [
      'publishResults',
      () =>
        admin.publishResults({
          academicYear: '2026/27',
          semester: 'autumn',
          publishAt: '2026-10-05T08:00:00.000Z',
          announce: true,
        }),
      'POST',
      '/api/admin/results/publish',
    ],
    [
      'reschedulePublication',
      () => admin.reschedulePublication('p1', { publishAt: 'x' }),
      'PUT',
      '/api/admin/results/publications/p1',
    ],
    [
      'cancelPublication',
      () => admin.cancelPublication('p1'),
      'DELETE',
      '/api/admin/results/publications/p1',
    ],
    [
      'unpublishPublication',
      () => admin.unpublishPublication('p1', { reason: 'Exam board recall' }),
      'POST',
      '/api/admin/results/publications/p1/unpublish',
    ],
    [
      'returnModuleToDraft',
      () =>
        admin.returnModuleToDraft('CS3099', {
          reason: 'Marks need review',
          academicYear: '2026/27',
        }),
      'POST',
      '/api/admin/results/modules/CS3099/return-to-draft',
    ],
    [
      'correctMark',
      () =>
        admin.correctMark('CS3099', 'S000002', { outcome: 'mark', mark: 70, reason: 'Recount' }),
      'POST',
      '/api/admin/results/modules/CS3099/marks/S000002/correct',
    ],
    [
      'createStudent',
      () =>
        admin.createStudent({
          studentNumber: 'S000123',
          fullName: 'A',
          programme: 'B',
          yearOfStudy: 1,
        }),
      'POST',
      '/api/admin/students',
    ],
    [
      'updateStudent',
      () =>
        admin.updateStudent('S000123', {
          fullName: 'A',
          programme: 'B',
          yearOfStudy: 1,
          email: null,
        }),
      'PUT',
      '/api/admin/students/S000123',
    ],
    [
      'markStudentLeft',
      () => admin.markStudentLeft('S000123', { reason: 'Withdrew formally' }),
      'POST',
      '/api/admin/students/S000123/leave',
    ],
    [
      'overrideEnrol',
      () =>
        admin.overrideEnrol('S000123', {
          moduleCode: 'CS3099',
          reason: 'Late registration',
          forceCapacity: true,
        }),
      'POST',
      '/api/admin/students/S000123/enrolments',
    ],
    [
      'overrideWithdraw',
      () => admin.overrideWithdraw('S000123', 'CS3099', { reason: 'Programme change' }),
      'POST',
      '/api/admin/students/S000123/enrolments/CS3099/withdraw',
    ],
    [
      'createModule',
      () =>
        admin.createModule({
          code: 'CS9999',
          title: 'T',
          credits: 15,
          capacity: 30,
          semester: 'autumn',
        }),
      'POST',
      '/api/admin/modules',
    ],
    [
      'updateModule',
      () =>
        admin.updateModule('CS9999', {
          title: 'T',
          description: null,
          credits: 15,
          capacity: 30,
          semester: 'autumn',
          isActive: true,
        }),
      'PUT',
      '/api/admin/modules/CS9999',
    ],
    [
      'trimModule',
      () => admin.trimModule('CS3099', { reason: 'Oversold in v0' }),
      'POST',
      '/api/admin/modules/CS3099/trim-to-capacity',
    ],
    [
      'setModuleLecturers',
      () =>
        admin.setModuleLecturers('CS3099', {
          assignments: [{ staffNumber: 'L00001', role: 'leader' }],
        }),
      'PUT',
      '/api/admin/modules/CS3099/lecturers',
    ],
    [
      'createLecturer',
      () =>
        admin.createLecturer({
          staffNumber: 'L00041',
          fullName: 'A',
          title: 'Dr',
          department: 'CS',
        }),
      'POST',
      '/api/admin/lecturers',
    ],
    [
      'updateLecturer',
      () =>
        admin.updateLecturer('L00041', {
          fullName: 'A',
          title: 'Dr',
          department: 'CS',
          email: null,
        }),
      'PUT',
      '/api/admin/lecturers/L00041',
    ],
    [
      'markLecturerLeft',
      () => admin.markLecturerLeft('L00041', { reason: 'Retired this term' }),
      'POST',
      '/api/admin/lecturers/L00041/leave',
    ],
    [
      'provisionAccount',
      () => admin.provisionAccount({ username: 'a', displayName: 'A', role: 'Admin' }),
      'POST',
      '/api/admin/accounts',
    ],
    ['lock', () => admin.accountAction('id1', 'lock'), 'POST', '/api/admin/accounts/id1/lock'],
    [
      'unlock',
      () => admin.accountAction('id1', 'unlock'),
      'POST',
      '/api/admin/accounts/id1/unlock',
    ],
    [
      'disable',
      () => admin.accountAction('id1', 'disable'),
      'POST',
      '/api/admin/accounts/id1/disable',
    ],
    [
      'enable',
      () => admin.accountAction('id1', 'enable'),
      'POST',
      '/api/admin/accounts/id1/enable',
    ],
    [
      'reset-mfa',
      () => admin.accountAction('id1', 'reset-mfa'),
      'POST',
      '/api/admin/accounts/id1/reset-mfa',
    ],
    [
      'resetAccountPassword',
      () => admin.resetAccountPassword('id1'),
      'POST',
      '/api/admin/accounts/id1/reset-password',
    ],
    [
      'createAnnouncement',
      () => admin.createAnnouncement({ title: 'T', body: 'B' }),
      'POST',
      '/api/admin/announcements',
    ],
    [
      'updateAnnouncement',
      () => admin.updateAnnouncement('a1', { title: 'T', body: 'B' }),
      'PUT',
      '/api/admin/announcements/a1',
    ],
    [
      'deleteAnnouncement',
      () => admin.deleteAnnouncement('a1'),
      'DELETE',
      '/api/admin/announcements/a1',
    ],
  ])('%s → %s %s with the request token', async (_name, call, method, path) => {
    await call()
    expect(last()).toMatchObject({ method, path, csrf: 'token' })
  })

  it('sends JSON bodies as the route table spells them', async () => {
    await admin.overrideEnrol('S000123', { moduleCode: 'CS3099', reason: 'Late registration' })
    expect(last().body).toEqual({ moduleCode: 'CS3099', reason: 'Late registration' })
    await admin.resetAccountPassword('id1')
    expect(last().body).toEqual({})
  })

  it('downloads the audit CSV with the same filters and no paging', async () => {
    const { headers } = await admin.exportAudit({ actor: 'admin', from: 'f', to: 't' })
    expect(last()).toMatchObject({
      method: 'GET',
      path: '/api/admin/audit/export.csv',
      search: '?actor=admin&from=f&to=t',
    })
    expect(headers.get('X-RushDay-Truncated')).toBe('true')
    expect(admin.studentExportPath('S000123')).toBe('/api/admin/students/S000123/export.json')
  })
})
