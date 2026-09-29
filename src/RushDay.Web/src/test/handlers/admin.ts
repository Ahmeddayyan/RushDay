import { http, HttpResponse } from 'msw'

import type {
  AdminLecturer,
  AdminMarksRow,
  AdminMarksSheet,
  AdminModule,
  AdminOverview,
  AdminResults,
  AdminResultsModule,
  AdminSettings,
  AdminStudentRow,
  AdminStudentView,
  CorrectMarkRequest,
  CreateLecturerRequest,
  CreateModuleRequest,
  CreateStudentRequest,
  CreateWindowRequest,
  OverrideEnrolRequest,
  ProvisionAccountRequest,
  PublishExclusion,
  PublishRequest,
  RosterEntry,
  UpdateModuleRequest,
  UpdateSettingsRequest,
  UpdateStudentRequest,
  UpdateWindowRequest,
} from '@/api/types/admin'
import type {
  AccountView,
  AnnouncementView,
  AuditEventView,
  MarksStatus,
  ModuleDetail,
  PublicationInfo,
  Semester,
  WindowInfo,
} from '@/api/types/common'

import { makeAccountView, makeAnnouncement, makeAuditEvent, makeModuleSummary } from '../factories'
import { hasCsrf, problem } from '../http'
import { authMock } from './auth'

/**
 * MSW handlers for the administrator routes of 02-api.md section 8.5, over a small in-memory
 * registry (`createAdminMock`), plus builders for the admin shapes. Tests pass
 * `createAdminMock(...).handlers` to `renderWithProviders`/`renderRoutes`, which call `server.use`;
 * `mock.requests` records every call for assertions. Instants are written as the API writes them:
 * ISO-8601 UTC with exactly three fractional digits.
 */

let sequence = 0
const nextId = () => {
  sequence += 1
  return `00000000-0000-7000-9000-${String(sequence).padStart(12, '0')}`
}

export const iso = (value: string | number | Date) => new Date(value).toISOString()

/** The mock's clock: 29 September 2026 12:00 UTC, inside the demo's autumn window. */
export const MOCK_NOW = Date.parse('2026-09-29T12:00:00.000Z')

// -------------------------------------------------------------------------------------------------
// Builders

export function makeMarksStatus(overrides: Partial<MarksStatus> = {}): MarksStatus {
  return {
    status: 'draft',
    entered: 0,
    missing: 20,
    total: 20,
    submittedAt: null,
    publishedAt: null,
    ...overrides,
  }
}

export function makeAdminSettings(overrides: Partial<AdminSettings> = {}): AdminSettings {
  return {
    academicYear: '2026/27',
    currentSemester: 'autumn',
    institutionName: 'Northbridge University',
    institutionShortName: 'Northbridge',
    timeZone: 'Europe/London',
    supportEmail: null,
    supportUrl: null,
    updatedAt: '2026-09-01T09:00:00.000Z',
    ...overrides,
  }
}

export function makeAdminStudentRow(overrides: Partial<AdminStudentRow> = {}): AdminStudentRow {
  return {
    studentNumber: 'S000002',
    fullName: 'Ben Carter',
    programme: 'BSc Computer Science',
    yearOfStudy: 2,
    email: 's000002@students.example.ac.uk',
    leftAt: null,
    accountState: 'active',
    ...overrides,
  }
}

export function makeAdminStudentView(overrides: Partial<AdminStudentView> = {}): AdminStudentView {
  return {
    student: {
      studentNumber: 'S000002',
      fullName: 'Ben Carter',
      programme: 'BSc Computer Science',
      yearOfStudy: 2,
      email: 's000002@students.example.ac.uk',
      leftAt: null,
    },
    account: makeAccountView(),
    enrolments: [
      {
        moduleCode: 'CS3001',
        title: 'Distributed Systems',
        credits: 15,
        semester: 'autumn',
        academicYear: '2026/27',
        status: 'active',
        source: 'self',
        enrolledAt: '2026-09-14T10:00:00.000Z',
        withdrawnAt: null,
      },
      {
        moduleCode: 'CS2001',
        title: 'Algorithms and Data Structures',
        credits: 15,
        semester: 'autumn',
        academicYear: '2025/26',
        status: 'active',
        source: 'seed',
        enrolledAt: '2025-09-15T10:00:00.000Z',
        withdrawnAt: null,
      },
    ],
    grades: [
      {
        moduleCode: 'CS2001',
        moduleTitle: 'Algorithms and Data Structures',
        credits: 15,
        semester: 'autumn',
        academicYear: '2025/26',
        outcome: 'mark',
        mark: 68,
        status: 'published',
        publishedAt: '2026-01-26T09:00:00.000Z',
        visibleToStudent: true,
        version: 3,
        correctedAt: null,
      },
    ],
    weightedAverage: 68,
    classification: '2:1',
    recentAudit: [
      makeAuditEvent({
        action: 'student.viewed',
        subjectType: 'Student',
        studentNumber: 'S000002',
        details: { studentNumber: 'S000002' },
      }),
    ],
    ...overrides,
  }
}

export function makeAdminModule(overrides: Partial<AdminModule> = {}): AdminModule {
  return {
    ...makeModuleSummary(),
    description: 'A team project run like a small software company.',
    marks: makeMarksStatus(),
    ...overrides,
  }
}

export function makeResultsModule(
  code: string,
  marks: Partial<MarksStatus> = {},
  overrides: Partial<AdminResultsModule> = {},
): AdminResultsModule {
  const status = makeMarksStatus(marks)
  return {
    code,
    title: `Module ${code}`,
    leader: 'Dr Aisha Khan',
    enrolledCount: status.total,
    marks: status,
    ...overrides,
  }
}

export function makePublicationInfo(overrides: Partial<PublicationInfo> = {}): PublicationInfo {
  return {
    id: nextId(),
    academicYear: '2026/27',
    semester: 'autumn',
    publishAt: '2026-10-05T08:00:00.000Z',
    state: 'scheduled',
    gradeCount: 120,
    moduleCount: 3,
    createdAt: '2026-09-28T15:00:00.000Z',
    createdBy: 'registry.admin',
    note: null,
    ...overrides,
  }
}

export function makeAdminResults(overrides: Partial<AdminResults> = {}): AdminResults {
  return {
    academicYear: '2026/27',
    semester: 'autumn',
    modules: [
      makeResultsModule('CS3001', { status: 'submitted', entered: 100, missing: 0, total: 100 }),
      makeResultsModule('CS3099', { status: 'submitted', entered: 28, missing: 2, total: 30 }),
      makeResultsModule('MA1001', { status: 'draft', entered: 10, missing: 30, total: 40 }),
      makeResultsModule('PH2001', { status: 'noStudents', entered: 0, missing: 0, total: 0 }),
    ],
    publications: [],
    ...overrides,
  }
}

export function makeAdminLecturer(overrides: Partial<AdminLecturer> = {}): AdminLecturer {
  return {
    staffNumber: 'L00001',
    fullName: 'Aisha Khan',
    title: 'Dr',
    department: 'CS',
    email: 'a.khan@example.ac.uk',
    leftAt: null,
    hasAccount: true,
    moduleCodes: ['CS3001', 'CS3099'],
    ...overrides,
  }
}

export function makeRosterEntry(index: number, overrides: Partial<RosterEntry> = {}): RosterEntry {
  return {
    studentNumber: `S${String(index + 1).padStart(6, '0')}`,
    fullName: `Student ${index + 1}`,
    programme: 'BSc Computer Science',
    yearOfStudy: 1,
    status: 'active',
    enrolledAt: '2026-09-14T10:00:00.000Z',
    withdrawnAt: null,
    ...overrides,
  }
}

export function makeAdminMarksRow(
  index: number,
  overrides: Partial<AdminMarksRow> = {},
): AdminMarksRow {
  return {
    studentNumber: `S${String(index + 1).padStart(6, '0')}`,
    fullName: `Student ${index + 1}`,
    enrolmentStatus: 'active',
    outcome: 'mark',
    mark: 50 + index,
    gradeStatus: 'submitted',
    version: 2,
    updatedAt: '2026-09-20T10:00:00.000Z',
    enteredBy: 'Dr Aisha Khan',
    correctedAt: null,
    ...overrides,
  }
}

export function makeAdminMarksSheet(
  overrides: Partial<AdminMarksSheet> = {},
  rowCount = 3,
): AdminMarksSheet {
  const rows = Array.from({ length: rowCount }, (_, index) => makeAdminMarksRow(index))
  return {
    code: 'CS3001',
    title: 'Distributed Systems',
    academicYear: '2026/27',
    status: 'submitted',
    submittedAt: '2026-09-25T10:00:00.000Z',
    publishedAt: null,
    myRole: null,
    leader: 'Dr Aisha Khan',
    summary: { entered: rowCount, missing: 0, total: rowCount },
    rows,
    page: 1,
    pageSize: 100,
    total: rowCount,
    ...overrides,
  }
}

export function makeAdminOverview(overrides: Partial<AdminOverview> = {}): AdminOverview {
  return {
    counts: {
      students: 20000,
      lecturers: 40,
      modules: 121,
      activeEnrolments: 100,
      accounts: 20041,
      lockedAccounts: 2,
      disabledAccounts: 5,
    },
    academicYear: '2026/27',
    enrolmentWindows: [
      {
        id: nextId(),
        academicYear: '2026/27',
        semester: 'autumn',
        opensAt: '2026-09-14T09:00:00.000Z',
        closesAt: '2026-10-02T17:00:00.000Z',
        withdrawalDeadlineAt: '2026-10-30T17:00:00.000Z',
        state: 'open',
      },
    ],
    nextPublication: null,
    latestPublication: makePublicationInfo({
      academicYear: '2025/26',
      publishAt: '2026-09-28T09:00:00.000Z',
      state: 'live',
      gradeCount: 80000,
      moduleCount: 60,
    }),
    submissionProgress: [
      {
        semester: 'autumn',
        modulesTotal: 12,
        noStudents: 49,
        draft: 10,
        submitted: 1,
        scheduled: 0,
        published: 1,
      },
    ],
    recentAudit: [makeAuditEvent()],
    database: 'ok',
    ...overrides,
  }
}

// -------------------------------------------------------------------------------------------------
// The in-memory registry

export interface RecordedRequest {
  method: string
  path: string
  search: URLSearchParams
  body: unknown
}

export interface AdminMockState {
  now: number
  settings: AdminSettings
  overview: AdminOverview
  accounts: AccountView[]
  students: AdminStudentRow[]
  studentViews: Record<string, AdminStudentView>
  lecturers: AdminLecturer[]
  modules: AdminModule[]
  moduleDetails: Record<string, ModuleDetail>
  rosters: Record<string, RosterEntry[]>
  marks: Record<string, AdminMarksSheet>
  results: AdminResults[]
  windows: WindowInfo[]
  announcements: AnnouncementView[]
  audit: AuditEventView[]
  /** The CSV export answers with `X-RushDay-Truncated: true` and the final comment line. */
  auditTruncatedAt: number | null
  /** The temporary password provision and reset answer with. */
  temporaryPassword: string
}

export interface AdminMock {
  state: AdminMockState
  handlers: ReturnType<typeof buildHandlers>
  requests: RecordedRequest[]
  /** The recorded requests to a method and path. */
  calls: (method: string, path: string) => RecordedRequest[]
}

function defaultState(): AdminMockState {
  return {
    now: MOCK_NOW,
    settings: makeAdminSettings(),
    overview: makeAdminOverview(),
    accounts: [
      makeAccountView({ id: nextId(), username: 'S000002', displayName: 'Ben Carter' }),
      makeAccountView({
        id: nextId(),
        username: 'S000001',
        displayName: 'Aisha Khan',
        studentNumber: 'S000001',
        isDemo: true,
      }),
    ],
    students: [makeAdminStudentRow()],
    studentViews: { S000002: makeAdminStudentView() },
    lecturers: [
      makeAdminLecturer(),
      makeAdminLecturer({
        staffNumber: 'L00006',
        fullName: 'Tom Price',
        hasAccount: false,
        moduleCodes: ['CS3099'],
      }),
    ],
    modules: [makeAdminModule()],
    moduleDetails: {},
    rosters: {},
    marks: {},
    results: [makeAdminResults()],
    windows: [makeAdminOverview().enrolmentWindows[0] as WindowInfo],
    announcements: [makeAnnouncement({ id: nextId() })],
    audit: [makeAuditEvent()],
    auditTruncatedAt: null,
    temporaryPassword: 'Kx7mPq2RtW9vNz4H',
  }
}

function paged<T>(items: readonly T[], search: URLSearchParams, defaultSize: number) {
  const page = Math.max(1, Number(search.get('page') ?? 1))
  const pageSize = Math.max(1, Number(search.get('pageSize') ?? defaultSize))
  return {
    items: items.slice((page - 1) * pageSize, page * pageSize),
    page,
    pageSize,
    total: items.length,
  }
}

function matchesQ(q: string | null, ...fields: (string | null)[]): boolean {
  if (!q) return true
  const needle = q.toLowerCase()
  return fields.some((field) => field !== null && field.toLowerCase().includes(needle))
}

function csrf(request: Request) {
  return hasCsrf(request) ? null : problem('antiforgery')
}

function detailOf(state: AdminMockState, code: string): ModuleDetail | undefined {
  const known = state.moduleDetails[code]
  if (known) return known
  const module = state.modules.find((item) => item.code === code)
  if (!module) return undefined
  const { marks: _marks, ...summary } = module
  void _marks
  return { ...summary, timetable: [] }
}

function resultsFor(state: AdminMockState, academicYear: string, semester: Semester): AdminResults {
  let entry = state.results.find(
    (item) => item.academicYear === academicYear && item.semester === semester,
  )
  if (!entry) {
    entry = { academicYear, semester, modules: [], publications: [] }
    state.results.push(entry)
  }
  return entry
}

function buildHandlers(state: AdminMockState, requests: RecordedRequest[]) {
  const record = async (request: Request) => {
    const url = new URL(request.url)
    let body: unknown = undefined
    if (request.method !== 'GET' && request.method !== 'DELETE') {
      const text = await request.clone().text()
      body = text ? (JSON.parse(text) as unknown) : undefined
    }
    requests.push({ method: request.method, path: url.pathname, search: url.searchParams, body })
    return { url, body }
  }
  const demoActor = () => authMock.user?.isDemo === true

  function accountMutation(
    id: string,
    change: (account: AccountView) => AccountView | Response,
  ): Response {
    const account = state.accounts.find((item) => item.id === id)
    if (!account) return problem('account-not-found')
    if (account.isDemo || demoActor()) {
      return problem('demo-account', { detail: 'Demo accounts are read-only' })
    }
    const next = change(account)
    if (next instanceof Response) return next
    state.accounts = state.accounts.map((item) => (item.id === id ? next : item))
    return HttpResponse.json(next)
  }

  return [
    // Overview and settings -----------------------------------------------------------------------
    http.get('/api/admin/overview', async ({ request }) => {
      await record(request)
      return HttpResponse.json(state.overview)
    }),
    http.get('/api/admin/settings', async ({ request }) => {
      await record(request)
      return HttpResponse.json(state.settings)
    }),
    http.put('/api/admin/settings', async ({ request }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as UpdateSettingsRequest
      state.settings = {
        ...state.settings,
        ...input,
        supportEmail: input.supportEmail ?? null,
        supportUrl: input.supportUrl ?? null,
        updatedAt: iso(state.now),
      }
      return HttpResponse.json(state.settings)
    }),

    // Enrolment windows ---------------------------------------------------------------------------
    http.get('/api/admin/enrolment-windows', async ({ request }) => {
      await record(request)
      return HttpResponse.json(state.windows)
    }),
    http.post('/api/admin/enrolment-windows', async ({ request }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as CreateWindowRequest
      if (
        state.windows.some(
          (item) => item.academicYear === input.academicYear && item.semester === input.semester,
        )
      ) {
        return problem('window-exists')
      }
      if (input.opensAt >= input.closesAt || input.withdrawalDeadlineAt < input.closesAt) {
        return problem('window-dates-invalid')
      }
      const window: WindowInfo = { id: nextId(), ...input, state: 'notYetOpen' }
      state.windows = [window, ...state.windows]
      return HttpResponse.json(window, { status: 201 })
    }),
    http.put('/api/admin/enrolment-windows/:id', async ({ request, params }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as UpdateWindowRequest
      const window = state.windows.find((item) => item.id === params.id)
      if (!window) return problem('window-not-found')
      if (input.opensAt >= input.closesAt || input.withdrawalDeadlineAt < input.closesAt) {
        return problem('window-dates-invalid')
      }
      const opens = Date.parse(input.opensAt)
      const closes = Date.parse(input.closesAt)
      const next: WindowInfo = {
        ...window,
        ...input,
        state: state.now < opens ? 'notYetOpen' : state.now < closes ? 'open' : 'closed',
      }
      state.windows = state.windows.map((item) => (item.id === window.id ? next : item))
      return HttpResponse.json(next)
    }),
    http.delete('/api/admin/enrolment-windows/:id', async ({ request, params }) => {
      await record(request)
      const denied = csrf(request)
      if (denied) return denied
      if (!state.windows.some((item) => item.id === params.id)) return problem('window-not-found')
      state.windows = state.windows.filter((item) => item.id !== params.id)
      return new HttpResponse(null, { status: 204 })
    }),

    // Results -------------------------------------------------------------------------------------
    http.get('/api/admin/results', async ({ request }) => {
      const { url } = await record(request)
      const semester = url.searchParams.get('semester')
      if (semester !== 'autumn' && semester !== 'spring') {
        return problem('validation', { extensions: { errors: { semester: ['Required'] } } })
      }
      const year = url.searchParams.get('academicYear') ?? state.settings.academicYear
      return HttpResponse.json(resultsFor(state, year, semester))
    }),
    http.post('/api/admin/results/publish', async ({ request }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as PublishRequest
      const publishAt = Math.max(Date.parse(input.publishAt), state.now)
      if (publishAt > state.now + 90 * 24 * 3600 * 1000) return problem('publish-too-far-ahead')
      const results = resultsFor(state, input.academicYear, input.semester)
      const publishable = results.modules.filter(
        (module) => module.marks.status === 'submitted' && module.marks.missing === 0,
      )
      if (publishable.length === 0) return problem('nothing-to-publish')
      const live = publishAt <= state.now
      const grades = publishable.reduce((sum, module) => sum + module.marks.entered, 0)
      const publication = makePublicationInfo({
        academicYear: input.academicYear,
        semester: input.semester,
        publishAt: iso(publishAt),
        state: live ? 'live' : 'scheduled',
        gradeCount: grades,
        moduleCount: publishable.length,
        createdAt: iso(state.now),
        note: input.note ?? null,
      })
      const excluded = results.modules.flatMap<PublishExclusion>((module) => {
        const { status, missing } = module.marks
        if (status === 'draft') {
          return [{ code: module.code, status, reason: 'notSubmitted', marksMissing: missing }]
        }
        if (status === 'submitted' && missing > 0) {
          return [{ code: module.code, status, reason: 'marksMissing', marksMissing: missing }]
        }
        return []
      })
      results.modules = results.modules.map((module) =>
        publishable.includes(module)
          ? {
              ...module,
              marks: {
                ...module.marks,
                status: live ? 'published' : 'scheduled',
                publishedAt: iso(publishAt),
              },
            }
          : module,
      )
      results.publications = [publication, ...results.publications]
      return HttpResponse.json({
        publication,
        published: { modules: publishable.length, grades },
        excluded,
      })
    }),
    http.put('/api/admin/results/publications/:id', async ({ request, params }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const { publishAt } = body as { publishAt: string }
      for (const results of state.results) {
        const publication = results.publications.find((item) => item.id === params.id)
        if (!publication) continue
        if (publication.state === 'live') return problem('publication-live')
        if (Date.parse(publishAt) > state.now + 90 * 24 * 3600 * 1000) {
          return problem('publish-too-far-ahead')
        }
        const next = { ...publication, publishAt: iso(publishAt) }
        results.publications = results.publications.map((item) =>
          item.id === publication.id ? next : item,
        )
        return HttpResponse.json(next)
      }
      return problem('publication-not-found')
    }),
    http.delete('/api/admin/results/publications/:id', async ({ request, params }) => {
      await record(request)
      const denied = csrf(request)
      if (denied) return denied
      for (const results of state.results) {
        const publication = results.publications.find((item) => item.id === params.id)
        if (!publication) continue
        if (publication.state === 'live') return problem('publication-live')
        results.publications = results.publications.filter((item) => item.id !== publication.id)
        results.modules = results.modules.map((module) =>
          module.marks.status === 'scheduled'
            ? { ...module, marks: { ...module.marks, status: 'submitted', publishedAt: null } }
            : module,
        )
        return HttpResponse.json({
          academicYear: publication.academicYear,
          semester: publication.semester,
          grades: publication.gradeCount,
        })
      }
      return problem('publication-not-found')
    }),
    http.post('/api/admin/results/publications/:id/unpublish', async ({ request, params }) => {
      await record(request)
      const denied = csrf(request)
      if (denied) return denied
      for (const results of state.results) {
        const publication = results.publications.find((item) => item.id === params.id)
        if (!publication) continue
        if (publication.state === 'scheduled') return problem('publication-scheduled')
        results.publications = results.publications.filter((item) => item.id !== publication.id)
        results.modules = results.modules.map((module) =>
          module.marks.status === 'published'
            ? { ...module, marks: { ...module.marks, status: 'submitted', publishedAt: null } }
            : module,
        )
        return HttpResponse.json({
          academicYear: publication.academicYear,
          semester: publication.semester,
          grades: publication.gradeCount,
        })
      }
      return problem('publication-not-found')
    }),
    http.post('/api/admin/results/modules/:code/return-to-draft', async ({ request, params }) => {
      await record(request)
      const denied = csrf(request)
      if (denied) return denied
      for (const results of state.results) {
        const module = results.modules.find((item) => item.code === params.code)
        if (!module) continue
        if (module.marks.status === 'draft' || module.marks.status === 'noStudents') {
          return problem('module-not-submitted')
        }
        if (module.marks.status === 'published') return problem('module-locked')
        const fromScheduledPublication = module.marks.status === 'scheduled'
        results.modules = results.modules.map((item) =>
          item.code === module.code
            ? {
                ...item,
                marks: { ...item.marks, status: 'draft', submittedAt: null, publishedAt: null },
              }
            : item,
        )
        return HttpResponse.json({ code: module.code, status: 'draft', fromScheduledPublication })
      }
      return problem('module-not-found')
    }),
    http.post(
      '/api/admin/results/modules/:code/marks/:studentNumber/correct',
      async ({ request, params }) => {
        const { body } = await record(request)
        const denied = csrf(request)
        if (denied) return denied
        const input = body as CorrectMarkRequest
        const sheet = state.marks[String(params.code)]
        const row = sheet?.rows.find((item) => item.studentNumber === params.studentNumber)
        if (!sheet || !row) return problem('grade-not-found')
        if (row.gradeStatus === 'draft' || row.gradeStatus === null) {
          return problem('module-not-submitted')
        }
        const after = { mark: input.mark, outcome: input.outcome ?? 'mark' }
        const before = { mark: row.mark, outcome: row.outcome ?? 'mark' }
        const correctedAt = iso(state.now)
        sheet.rows = sheet.rows.map((item) =>
          item === row
            ? { ...item, ...after, version: (item.version ?? 0) + 1, correctedAt }
            : item,
        )
        return HttpResponse.json({
          studentNumber: row.studentNumber,
          before,
          after,
          version: (row.version ?? 0) + 1,
          correctedAt,
        })
      },
    ),

    // Students ------------------------------------------------------------------------------------
    http.get('/api/admin/students', async ({ request }) => {
      const { url } = await record(request)
      const q = url.searchParams.get('q')
      const accountState = url.searchParams.get('accountState')
      const rows = state.students.filter(
        (student) =>
          (!q ||
            student.studentNumber.toLowerCase().startsWith(q.toLowerCase()) ||
            matchesQ(q, student.fullName)) &&
          (!accountState || student.accountState === accountState),
      )
      return HttpResponse.json(paged(rows, url.searchParams, 25))
    }),
    http.post('/api/admin/students', async ({ request }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as CreateStudentRequest
      if (state.students.some((item) => item.studentNumber === input.studentNumber)) {
        return problem('student-number-taken')
      }
      const row: AdminStudentRow = {
        studentNumber: input.studentNumber,
        fullName: input.fullName,
        programme: input.programme,
        yearOfStudy: input.yearOfStudy,
        email: input.email ?? null,
        leftAt: null,
        accountState: 'none',
      }
      state.students = [...state.students, row]
      return HttpResponse.json(row, { status: 201 })
    }),
    http.get('/api/admin/students/:studentNumber/export.json', async ({ request, params }) => {
      await record(request)
      const view = state.studentViews[String(params.studentNumber)]
      if (!view) return problem('student-not-found')
      const { recentAudit: _audit, account: _account, ...rest } = view
      void _audit
      void _account
      return HttpResponse.json(
        { ...rest, exportedAt: iso(state.now) },
        {
          headers: {
            'Content-Disposition': `attachment; filename="rushday-${view.student.studentNumber}.json"`,
          },
        },
      )
    }),
    http.get('/api/admin/students/:studentNumber', async ({ request, params }) => {
      await record(request)
      const view = state.studentViews[String(params.studentNumber)]
      return view ? HttpResponse.json(view) : problem('student-not-found')
    }),
    http.put('/api/admin/students/:studentNumber', async ({ request, params }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as UpdateStudentRequest
      const view = state.studentViews[String(params.studentNumber)]
      if (!view) return problem('student-not-found')
      view.student = { ...view.student, ...input }
      const row = { ...makeAdminStudentRow(), ...view.student, accountState: 'active' as const }
      return HttpResponse.json(row)
    }),
    http.post('/api/admin/students/:studentNumber/leave', async ({ request, params }) => {
      await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const view = state.studentViews[String(params.studentNumber)]
      if (!view) return problem('student-not-found')
      if (view.student.leftAt) return problem('student-left')
      const withdrawn = view.enrolments.filter(
        (item) => item.status === 'active' && item.academicYear === state.settings.academicYear,
      ).length
      view.student = { ...view.student, leftAt: iso(state.now) }
      return HttpResponse.json({
        studentNumber: view.student.studentNumber,
        leftAt: iso(state.now),
        withdrawn,
      })
    }),
    http.post('/api/admin/students/:studentNumber/enrolments', async ({ request, params }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as OverrideEnrolRequest
      const view = state.studentViews[String(params.studentNumber)]
      if (!view) return problem('student-not-found')
      const module = state.modules.find((item) => item.code === input.moduleCode)
      if (!module) return problem('module-not-found')
      if (module.placesRemaining === 0 && !input.forceCapacity) return problem('module-full')
      const capacityRaised = module.placesRemaining === 0
      return HttpResponse.json(
        {
          moduleCode: module.code,
          enrolledAt: iso(state.now),
          placesRemaining: Math.max(0, module.placesRemaining - 1),
          capacityRaised,
        },
        { status: 201 },
      )
    }),
    http.post(
      '/api/admin/students/:studentNumber/enrolments/:code/withdraw',
      async ({ request, params }) => {
        await record(request)
        const denied = csrf(request)
        if (denied) return denied
        const view = state.studentViews[String(params.studentNumber)]
        const enrolment = view?.enrolments.find(
          (item) => item.moduleCode === params.code && item.status === 'active',
        )
        if (!view || !enrolment) return problem('not-enrolled')
        view.enrolments = view.enrolments.map((item) =>
          item === enrolment ? { ...item, status: 'withdrawn', withdrawnAt: iso(state.now) } : item,
        )
        return new HttpResponse(null, { status: 204 })
      },
    ),

    // Modules and lecturers -----------------------------------------------------------------------
    http.get('/api/admin/modules', async ({ request }) => {
      const { url } = await record(request)
      const includeInactive = url.searchParams.get('includeInactive') === 'true'
      return HttpResponse.json(state.modules.filter((module) => includeInactive || module.isActive))
    }),
    http.post('/api/admin/modules', async ({ request }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as CreateModuleRequest
      if (state.modules.some((item) => item.code === input.code))
        return problem('module-code-taken')
      const module = makeAdminModule({
        ...input,
        description: input.description ?? null,
        enrolledCount: 0,
        placesRemaining: input.capacity,
        lecturers: [],
        marks: makeMarksStatus({ status: 'noStudents', missing: 0, total: 0 }),
      })
      state.modules = [...state.modules, module]
      return HttpResponse.json(detailOf(state, module.code), { status: 201 })
    }),
    http.put('/api/admin/modules/:code/lecturers', async ({ request, params }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const { assignments } = body as {
        assignments: { staffNumber: string; role: 'leader' | 'teacher' }[]
      }
      const module = state.modules.find((item) => item.code === params.code)
      if (!module) return problem('module-not-found')
      const leaders = assignments.filter((item) => item.role === 'leader').length
      const numbers = new Set(assignments.map((item) => item.staffNumber))
      if (leaders !== 1 || numbers.size !== assignments.length) {
        return problem('invalid-lecturer-assignment')
      }
      const lecturers = assignments.map((assignment) => {
        const lecturer = state.lecturers.find((item) => item.staffNumber === assignment.staffNumber)
        return {
          staffNumber: assignment.staffNumber,
          fullName: lecturer?.fullName ?? assignment.staffNumber,
          title: lecturer?.title ?? 'Dr',
          role: assignment.role,
          left: lecturer?.leftAt != null,
        }
      })
      state.modules = state.modules.map((item) =>
        item.code === module.code ? { ...item, lecturers } : item,
      )
      return HttpResponse.json(detailOf(state, module.code))
    }),
    http.put('/api/admin/modules/:code', async ({ request, params }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as UpdateModuleRequest
      const module = state.modules.find((item) => item.code === params.code)
      if (!module) return problem('module-not-found')
      if (input.capacity !== module.capacity && input.capacity < module.enrolledCount) {
        return problem('capacity-below-enrolled', {
          extensions: { enrolledCount: module.enrolledCount },
        })
      }
      if (input.semester !== module.semester && module.enrolledCount > 0) {
        return problem('semester-change-with-enrolments', {
          extensions: { enrolledCount: module.enrolledCount },
        })
      }
      const next = {
        ...module,
        ...input,
        placesRemaining: Math.max(0, input.capacity - module.enrolledCount),
      }
      state.modules = state.modules.map((item) => (item.code === module.code ? next : item))
      return HttpResponse.json(detailOf(state, module.code))
    }),
    http.post('/api/admin/modules/:code/trim-to-capacity', async ({ request, params }) => {
      await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const module = state.modules.find((item) => item.code === params.code)
      if (!module) return problem('module-not-found')
      const excess = Math.max(0, module.enrolledCount - module.capacity)
      state.modules = state.modules.map((item) =>
        item.code === module.code
          ? { ...item, enrolledCount: module.capacity, placesRemaining: 0 }
          : item,
      )
      return HttpResponse.json({
        code: module.code,
        capacity: module.capacity,
        before: module.enrolledCount,
        after: module.capacity,
        withdrawn: Array.from({ length: excess }, (_, index) => `S${String(900000 + index)}`),
      })
    }),
    http.get('/api/admin/modules/:code/roster', async ({ request, params }) => {
      const { url } = await record(request)
      const module = state.modules.find((item) => item.code === params.code)
      if (!module) return problem('module-not-found')
      const q = url.searchParams.get('q')
      const rows = (state.rosters[module.code] ?? []).filter((row) =>
        matchesQ(q, row.studentNumber, row.fullName),
      )
      const { marks: _marks, description: _description, ...summary } = module
      void _marks
      void _description
      return HttpResponse.json({ module: summary, ...paged(rows, url.searchParams, 50) })
    }),
    http.get('/api/admin/modules/:code/marks', async ({ request, params }) => {
      const { url } = await record(request)
      const sheet = state.marks[String(params.code)]
      if (!sheet) return problem('module-not-found')
      const q = url.searchParams.get('q')
      const rows = sheet.rows.filter((row) => matchesQ(q, row.studentNumber, row.fullName))
      const page = paged(rows, url.searchParams, 100)
      return HttpResponse.json({
        ...sheet,
        rows: page.items,
        page: page.page,
        pageSize: page.pageSize,
        total: page.total,
      })
    }),
    http.get('/api/modules/:code', async ({ request, params }) => {
      await record(request)
      const detail = detailOf(state, String(params.code))
      return detail ? HttpResponse.json(detail) : problem('module-not-found')
    }),
    http.get('/api/admin/lecturers', async ({ request }) => {
      const { url } = await record(request)
      const q = url.searchParams.get('q')
      return HttpResponse.json(
        state.lecturers.filter((lecturer) => matchesQ(q, lecturer.staffNumber, lecturer.fullName)),
      )
    }),
    http.post('/api/admin/lecturers', async ({ request }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as CreateLecturerRequest
      if (state.lecturers.some((item) => item.staffNumber === input.staffNumber)) {
        return problem('staff-number-taken')
      }
      const lecturer = makeAdminLecturer({
        ...input,
        email: input.email ?? null,
        hasAccount: false,
        moduleCodes: [],
      })
      state.lecturers = [...state.lecturers, lecturer]
      return HttpResponse.json(lecturer, { status: 201 })
    }),
    http.put('/api/admin/lecturers/:staffNumber', async ({ request, params }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const lecturer = state.lecturers.find((item) => item.staffNumber === params.staffNumber)
      if (!lecturer) return problem('lecturer-not-found')
      const next = { ...lecturer, ...(body as object) }
      state.lecturers = state.lecturers.map((item) => (item === lecturer ? next : item))
      return HttpResponse.json(next)
    }),
    http.post('/api/admin/lecturers/:staffNumber/leave', async ({ request, params }) => {
      await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const lecturer = state.lecturers.find((item) => item.staffNumber === params.staffNumber)
      if (!lecturer) return problem('lecturer-not-found')
      const next = { ...lecturer, leftAt: lecturer.leftAt ?? iso(state.now) }
      state.lecturers = state.lecturers.map((item) => (item === lecturer ? next : item))
      return HttpResponse.json(next)
    }),

    // Accounts ------------------------------------------------------------------------------------
    http.get('/api/admin/accounts', async ({ request }) => {
      const { url } = await record(request)
      const q = url.searchParams.get('q')
      const role = url.searchParams.get('role')
      const accountState = url.searchParams.get('state')
      const rows = state.accounts.filter(
        (account) =>
          matchesQ(q, account.username, account.displayName) &&
          (!role || account.role === role) &&
          (!accountState || account.state === accountState),
      )
      return HttpResponse.json(paged(rows, url.searchParams, 25))
    }),
    http.post('/api/admin/accounts', async ({ request }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as ProvisionAccountRequest
      if (
        (input.role === 'Student' && !input.studentNumber) ||
        (input.role === 'Lecturer' && !input.staffNumber) ||
        (input.role === 'Admin' && (input.studentNumber || input.staffNumber))
      ) {
        return problem('role-principal-mismatch')
      }
      if (
        state.accounts.some((item) => item.username.toLowerCase() === input.username.toLowerCase())
      ) {
        return problem('username-taken')
      }
      if (
        state.accounts.some(
          (item) =>
            (input.studentNumber && item.studentNumber === input.studentNumber) ||
            (input.staffNumber && item.staffNumber === input.staffNumber),
        )
      ) {
        return problem('principal-has-account')
      }
      const account = makeAccountView({
        id: nextId(),
        username: input.username,
        displayName: input.displayName,
        role: input.role,
        studentNumber: input.studentNumber ?? null,
        staffNumber: input.staffNumber ?? null,
        email: input.email ?? null,
        mustChangePassword: !demoActor(),
        isDemo: demoActor(),
        createdAt: iso(state.now),
        lastLoginAt: null,
      })
      state.accounts = [...state.accounts, account]
      state.students = state.students.map((student) =>
        student.studentNumber === input.studentNumber
          ? { ...student, accountState: 'active' }
          : student,
      )
      return HttpResponse.json(
        { account, temporaryPassword: input.temporaryPassword ?? state.temporaryPassword },
        { status: 201 },
      )
    }),
    http.post('/api/admin/accounts/:id/reset-password', async ({ request, params }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = (body ?? {}) as { temporaryPassword?: string }
      const response = accountMutation(String(params.id), (account) => ({
        ...account,
        mustChangePassword: true,
      }))
      if (!response.ok) return response
      return HttpResponse.json({
        temporaryPassword: input.temporaryPassword ?? state.temporaryPassword,
      })
    }),
    http.post('/api/admin/accounts/:id/:action', async ({ request, params }) => {
      await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const self = authMock.user?.id === params.id
      switch (params.action) {
        case 'lock':
          if (self) return problem('self-lockout')
          return accountMutation(String(params.id), (account) => ({
            ...account,
            state: account.state === 'disabled' ? 'disabled' : 'locked',
            lockoutEnd: '9999-12-31T00:00:00.000Z',
          }))
        case 'unlock':
          return accountMutation(String(params.id), (account) => ({
            ...account,
            state: account.state === 'disabled' ? 'disabled' : 'active',
            lockoutEnd: null,
          }))
        case 'disable':
          if (self) return problem('self-lockout')
          return accountMutation(String(params.id), (account) => ({
            ...account,
            state: 'disabled',
          }))
        case 'enable':
          return accountMutation(String(params.id), (account) => ({
            ...account,
            state: account.lockoutEnd ? 'locked' : 'active',
          }))
        case 'reset-mfa':
          return accountMutation(String(params.id), (account) => ({
            ...account,
            mfaEnabled: false,
          }))
        default:
          return problem('not-found')
      }
    }),

    // Announcements -------------------------------------------------------------------------------
    http.get('/api/admin/announcements', async ({ request }) => {
      await record(request)
      return HttpResponse.json(state.announcements)
    }),
    http.post('/api/admin/announcements', async ({ request }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const input = body as {
        title: string
        body: string
        pinned?: boolean
        publishedAt?: string
        expiresAt?: string
      }
      const announcement = makeAnnouncement({
        id: nextId(),
        title: input.title,
        body: input.body,
        pinned: input.pinned ?? false,
        publishedAt: input.publishedAt ?? iso(state.now),
        expiresAt: input.expiresAt ?? null,
        author: authMock.user?.displayName ?? 'Registry',
      })
      state.announcements = [announcement, ...state.announcements]
      return HttpResponse.json(announcement, { status: 201 })
    }),
    http.put('/api/admin/announcements/:id', async ({ request, params }) => {
      const { body } = await record(request)
      const denied = csrf(request)
      if (denied) return denied
      const existing = state.announcements.find((item) => item.id === params.id)
      if (!existing) return problem('announcement-not-found')
      const next = { ...existing, ...(body as object), updatedAt: iso(state.now) }
      state.announcements = state.announcements.map((item) => (item === existing ? next : item))
      return HttpResponse.json(next)
    }),
    http.delete('/api/admin/announcements/:id', async ({ request, params }) => {
      await record(request)
      const denied = csrf(request)
      if (denied) return denied
      if (!state.announcements.some((item) => item.id === params.id)) {
        return problem('announcement-not-found')
      }
      state.announcements = state.announcements.filter((item) => item.id !== params.id)
      return new HttpResponse(null, { status: 204 })
    }),

    // Audit ---------------------------------------------------------------------------------------
    http.get('/api/admin/audit/export.csv', async ({ request }) => {
      await record(request)
      const header =
        'occurredAt,actorUsername,actorRole,action,subjectType,subjectId,studentNumber,moduleCode,details,requestId'
      const lines = state.audit.map((event) =>
        [event.occurredAt, event.actorUsername, event.actorRole, event.action, event.subjectType]
          .map((value) => value ?? '')
          .join(','),
      )
      const truncated = state.auditTruncatedAt !== null
      if (truncated) {
        lines.push(`# truncated at ${state.auditTruncatedAt} rows; narrow the date range`)
      }
      return new HttpResponse([header, ...lines].join('\n'), {
        headers: {
          'Content-Type': 'text/csv; charset=utf-8',
          'Content-Disposition': 'attachment; filename="audit-2026-09-01-2026-09-29.csv"',
          ...(truncated ? { 'X-RushDay-Truncated': 'true' } : {}),
        },
      })
    }),
    http.get('/api/admin/audit', async ({ request }) => {
      const { url } = await record(request)
      const search = url.searchParams
      const rows = state.audit.filter(
        (event) =>
          (!search.get('actor') || event.actorUsername === search.get('actor')) &&
          (!search.get('action') || event.action === search.get('action')) &&
          (!search.get('studentNumber') || event.studentNumber === search.get('studentNumber')) &&
          (!search.get('moduleCode') || event.moduleCode === search.get('moduleCode')),
      )
      return HttpResponse.json(paged(rows, search, 50))
    }),
  ]
}

/** A fresh in-memory registry and its handlers; `overrides` replace parts of the default state. */
export function createAdminMock(overrides: Partial<AdminMockState> = {}): AdminMock {
  const state: AdminMockState = { ...defaultState(), ...overrides }
  const requests: RecordedRequest[] = []
  return {
    state,
    requests,
    handlers: buildHandlers(state, requests),
    calls: (method, path) =>
      requests.filter((request) => request.method === method && request.path === path),
  }
}
