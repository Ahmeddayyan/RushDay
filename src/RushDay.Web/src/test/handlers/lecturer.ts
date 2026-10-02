import { http, HttpResponse } from 'msw'

import type { AnnouncementView, GradeOutcome } from '@/api/types/common'
import type {
  LecturerModuleSummary,
  MarksRow,
  MarksSheet,
  ModuleAnnouncementRequest,
  RosterEntry,
  SaveMarksRequest,
} from '@/api/types/lecturer'

import { hasCsrf, problem } from '../http'

/**
 * An in-memory stand-in for the `/api/lecturer` routes (02-api.md section 8.4), from the point of
 * view of the one signed-in lecturer a test renders as (`myRole` and `leader` are fixed per module at
 * seed time, rather than derived from a full lecturer list, which no S8 test needs).
 * `seedLecturerModule` sets up a module; `resetLecturerStore` runs after every test.
 */

export interface LecturerModuleFixture {
  summary: LecturerModuleSummary
  roster: RosterEntry[]
  rows: MarksRow[]
  leader: string
  announcements: AnnouncementView[]
}

const store = new Map<string, LecturerModuleFixture>()
let nextAnnouncementId = 1

/** The row count of every `PUT .../marks` call, in order: lets a test prove a large save was chunked. */
export const putMarksCallSizes: number[] = []

export function resetLecturerStore(): void {
  store.clear()
  nextAnnouncementId = 1
  putMarksCallSizes.length = 0
}

export function seedLecturerModule(fixture: LecturerModuleFixture): LecturerModuleFixture {
  store.set(fixture.summary.code, fixture)
  return fixture
}

export function getLecturerFixture(code: string): LecturerModuleFixture | undefined {
  return store.get(code)
}

function recomputeMarksStatus(fixture: LecturerModuleFixture): void {
  const active = fixture.rows.filter((row) => row.enrolmentStatus === 'active')
  const total = active.length
  const entered = active.filter((row) => row.gradeStatus !== null).length
  const missing = total - entered
  const anyPublished = active.some((row) => row.gradeStatus === 'published')
  const anySubmitted = active.some((row) => row.gradeStatus === 'submitted')
  const anyDraft = active.some((row) => row.gradeStatus === 'draft')
  const status =
    total === 0
      ? 'noStudents'
      : anyPublished
        ? 'published'
        : anySubmitted
          ? 'submitted'
          : anyDraft
            ? 'draft'
            : 'noStudents'
  fixture.summary.marks = {
    status,
    entered,
    missing,
    total,
    submittedAt: fixture.summary.marks.submittedAt,
    publishedAt: fixture.summary.marks.publishedAt,
  }
}

function toSheet(fixture: LecturerModuleFixture, page: number, pageSize: number, q: string): MarksSheet {
  const needle = q.trim().toLowerCase()
  const filtered = needle
    ? fixture.rows.filter(
        (row) =>
          row.studentNumber.toLowerCase().startsWith(needle) ||
          row.fullName.toLowerCase().includes(needle),
      )
    : fixture.rows
  const start = (page - 1) * pageSize
  return {
    code: fixture.summary.code,
    title: fixture.summary.title,
    academicYear: '2026/27',
    status: fixture.summary.marks.status,
    submittedAt: fixture.summary.marks.submittedAt,
    publishedAt: fixture.summary.marks.publishedAt,
    myRole: fixture.summary.myRole,
    leader: fixture.leader,
    summary: {
      entered: fixture.summary.marks.entered,
      missing: fixture.summary.marks.missing,
      total: fixture.summary.marks.total,
    },
    rows: filtered.slice(start, start + pageSize),
    page,
    pageSize,
    total: filtered.length,
  }
}

function requireFixture(code: string) {
  const fixture = store.get(code.toUpperCase())
  if (!fixture) return undefined
  return fixture
}

export const lecturerHandlers = [
  http.get('/api/lecturer/modules', () =>
    HttpResponse.json(Array.from(store.values()).map((fixture) => fixture.summary)),
  ),

  http.get('/api/lecturer/modules/:code/roster', ({ params, request }) => {
    const fixture = requireFixture(String(params.code))
    if (!fixture) return problem('not-your-module')
    const url = new URL(request.url)
    const q = url.searchParams.get('q') ?? ''
    const page = Number(url.searchParams.get('page') ?? '1')
    const pageSize = Number(url.searchParams.get('pageSize') ?? '50')
    const needle = q.trim().toLowerCase()
    const filtered = needle
      ? fixture.roster.filter(
          (row) =>
            row.studentNumber.toLowerCase().startsWith(needle) ||
            row.fullName.toLowerCase().includes(needle),
        )
      : fixture.roster
    const start = (page - 1) * pageSize
    return HttpResponse.json({
      module: fixture.summary,
      items: filtered.slice(start, start + pageSize),
      page,
      pageSize,
      total: filtered.length,
    })
  }),

  http.get('/api/lecturer/modules/:code/marks', ({ params, request }) => {
    const fixture = requireFixture(String(params.code))
    if (!fixture) return problem('not-your-module')
    const url = new URL(request.url)
    const q = url.searchParams.get('q') ?? ''
    const page = Number(url.searchParams.get('page') ?? '1')
    const pageSize = Number(url.searchParams.get('pageSize') ?? '100')
    return HttpResponse.json(toSheet(fixture, page, pageSize, q))
  }),

  http.put('/api/lecturer/modules/:code/marks', async ({ params, request }) => {
    if (!hasCsrf(request)) return problem('antiforgery')
    const fixture = requireFixture(String(params.code))
    if (!fixture) return problem('not-your-module')
    if (fixture.summary.marks.status !== 'draft' && fixture.summary.marks.status !== 'noStudents') {
      return problem('module-locked')
    }
    const body = (await request.json()) as SaveMarksRequest
    // 02-api.md section 8.4: 1..500 rows; enforced here so a test can prove the client really
    // chunked a larger save into sequential requests, rather than sending it all in one call.
    putMarksCallSizes.push(body.rows.length)
    if (body.rows.length > 500 || body.rows.length < 1) return problem('validation')
    const byNumber = new Map(fixture.rows.map((row) => [row.studentNumber, row]))

    const notEnrolled = body.rows.filter((row) => byNumber.get(row.studentNumber)?.enrolmentStatus !== 'active')
    if (notEnrolled.length > 0) {
      return problem('not-enrolled-students', {
        extensions: { studentNumbers: notEnrolled.map((row) => row.studentNumber) },
      })
    }

    const stale = body.rows.filter((row) => {
      const current = byNumber.get(row.studentNumber)
      return current?.version !== null && current?.version !== undefined && row.version !== current.version
    })
    if (stale.length > 0) {
      return problem('stale-mark', {
        extensions: { studentNumbers: stale.map((row) => row.studentNumber) },
      })
    }

    const now = new Date().toISOString()
    const saved: MarksRow[] = []
    for (const requested of body.rows) {
      const current = byNumber.get(requested.studentNumber)
      if (!current) continue
      const outcome: GradeOutcome = requested.outcome ?? 'mark'
      current.outcome = outcome
      current.mark = outcome === 'mark' ? requested.mark : null
      current.gradeStatus = 'draft'
      current.version = (current.version ?? 0) + 1
      current.updatedAt = now
      current.enteredBy = 'You'
      saved.push({ ...current })
    }
    recomputeMarksStatus(fixture)
    return HttpResponse.json({
      summary: {
        entered: fixture.summary.marks.entered,
        missing: fixture.summary.marks.missing,
        total: fixture.summary.marks.total,
      },
      rows: saved,
    })
  }),

  http.post('/api/lecturer/modules/:code/marks/submit', ({ params, request }) => {
    if (!hasCsrf(request)) return problem('antiforgery')
    const fixture = requireFixture(String(params.code))
    if (!fixture) return problem('not-your-module')
    if (fixture.summary.myRole !== 'leader') return problem('not-module-leader')
    const active = fixture.rows.filter((row) => row.enrolmentStatus === 'active')
    if (active.length === 0) return problem('nothing-to-submit')
    if (fixture.summary.marks.status === 'submitted' || fixture.summary.marks.status === 'published') {
      return fixture.summary.marks.status === 'submitted'
        ? problem('already-submitted')
        : problem('module-locked')
    }
    const missing = active.filter((row) => row.outcome === null).map((row) => row.studentNumber)
    if (missing.length > 0) return problem('marks-incomplete', { extensions: { missing } })

    const now = new Date().toISOString()
    for (const row of active) {
      row.gradeStatus = 'submitted'
      row.version = (row.version ?? 0) + 1
    }
    fixture.summary.marks = {
      ...fixture.summary.marks,
      status: 'submitted',
      submittedAt: now,
    }
    return HttpResponse.json({
      code: fixture.summary.code,
      status: 'submitted',
      submittedAt: now,
      gradeCount: active.length,
    })
  }),

  http.get('/api/lecturer/modules/:code/announcements', ({ params }) => {
    const fixture = requireFixture(String(params.code))
    if (!fixture) return problem('not-your-module')
    return HttpResponse.json(fixture.announcements)
  }),

  http.post('/api/lecturer/modules/:code/announcements', async ({ params, request }) => {
    if (!hasCsrf(request)) return problem('antiforgery')
    const fixture = requireFixture(String(params.code))
    if (!fixture) return problem('not-your-module')
    const body = (await request.json()) as ModuleAnnouncementRequest
    const now = new Date().toISOString()
    const announcement: AnnouncementView = {
      id: `announcement-${nextAnnouncementId++}`,
      scope: 'module',
      moduleCode: fixture.summary.code,
      title: body.title,
      body: body.body,
      pinned: body.pinned ?? false,
      publishedAt: body.publishedAt ?? now,
      expiresAt: body.expiresAt ?? null,
      author: 'You',
      createdAt: now,
      updatedAt: now,
    }
    fixture.announcements.unshift(announcement)
    return HttpResponse.json(announcement, { status: 201 })
  }),

  http.put('/api/lecturer/modules/:code/announcements/:id', async ({ params, request }) => {
    if (!hasCsrf(request)) return problem('antiforgery')
    const fixture = requireFixture(String(params.code))
    if (!fixture) return problem('not-your-module')
    const existing = fixture.announcements.find((item) => item.id === params.id)
    if (!existing) return problem('announcement-not-found')
    const body = (await request.json()) as ModuleAnnouncementRequest
    const now = new Date().toISOString()
    const updated: AnnouncementView = {
      ...existing,
      title: body.title,
      body: body.body,
      pinned: body.pinned ?? existing.pinned,
      publishedAt: body.publishedAt ?? existing.publishedAt,
      expiresAt: body.expiresAt ?? null,
      updatedAt: now,
    }
    fixture.announcements = fixture.announcements.map((item) => (item.id === updated.id ? updated : item))
    return HttpResponse.json(updated)
  }),

  http.delete('/api/lecturer/modules/:code/announcements/:id', ({ params, request }) => {
    if (!hasCsrf(request)) return problem('antiforgery')
    const fixture = requireFixture(String(params.code))
    if (!fixture) return problem('not-your-module')
    const existing = fixture.announcements.find((item) => item.id === params.id)
    if (!existing) return problem('announcement-not-found')
    fixture.announcements = fixture.announcements.filter((item) => item.id !== params.id)
    return new HttpResponse(null, { status: 204 })
  }),
]

// ---------------------------------------------------------------------------------------------
// Builders (05-frontend.md section 13.1: "keep your factories in your owned files"). Each S8 test
// seeds a module through `buildLecturerModule` and `seedLecturerModule`.

export function makeLecturerModuleSummary(
  overrides: Partial<LecturerModuleSummary> = {},
): LecturerModuleSummary {
  return {
    code: 'CS3001',
    title: 'Distributed Systems',
    department: 'CS',
    level: 3,
    credits: 15,
    semester: 'autumn',
    capacity: 120,
    enrolledCount: 3,
    placesRemaining: 117,
    isActive: true,
    lecturers: [
      { staffNumber: 'L00001', fullName: 'Grace Hopper', title: 'Dr', role: 'leader', left: false },
    ],
    enrolmentState: 'closed',
    windowOpensAt: null,
    windowClosesAt: null,
    withdrawalDeadlineAt: null,
    myRole: 'leader',
    marks: { status: 'draft', entered: 0, missing: 3, total: 3, submittedAt: null, publishedAt: null },
    ...overrides,
  }
}

export function makeRosterEntry(index: number, overrides: Partial<RosterEntry> = {}): RosterEntry {
  return {
    studentNumber: `S${String(index + 1).padStart(6, '0')}`,
    fullName: `Student ${index + 1}`,
    programme: 'BSc Computer Science',
    yearOfStudy: 3,
    status: 'active',
    enrolledAt: '2026-09-14T10:00:00.000Z',
    withdrawnAt: null,
    ...overrides,
  }
}

export function makeMarksRowFixture(index: number, overrides: Partial<MarksRow> = {}): MarksRow {
  return {
    studentNumber: `S${String(index + 1).padStart(6, '0')}`,
    fullName: `Student ${index + 1}`,
    enrolmentStatus: 'active',
    outcome: null,
    mark: null,
    gradeStatus: null,
    version: null,
    updatedAt: null,
    enteredBy: null,
    correctedAt: null,
    ...overrides,
  }
}

export interface BuildLecturerModuleOptions {
  code?: string
  title?: string
  rowCount?: number
  myRole?: 'leader' | 'teacher'
  leader?: string
  summaryOverrides?: Partial<LecturerModuleSummary>
  rows?: MarksRow[]
  roster?: RosterEntry[]
  announcements?: AnnouncementView[]
}

/** A module with `rowCount` active students, all ungraded, ready for a Draft marks grid. */
export function buildLecturerModule(options: BuildLecturerModuleOptions = {}): LecturerModuleFixture {
  const {
    code = 'CS3001',
    title = 'Distributed Systems',
    rowCount = 3,
    myRole = 'leader',
    leader = 'Dr Grace Hopper',
    summaryOverrides = {},
    rows = Array.from({ length: rowCount }, (_, index) => makeMarksRowFixture(index)),
    roster = Array.from({ length: rowCount }, (_, index) => makeRosterEntry(index)),
    announcements = [],
  } = options

  const total = rows.filter((row) => row.enrolmentStatus === 'active').length
  const entered = rows.filter(
    (row) => row.enrolmentStatus === 'active' && row.gradeStatus !== null,
  ).length

  const fixture: LecturerModuleFixture = {
    summary: makeLecturerModuleSummary({
      code,
      title,
      myRole,
      enrolledCount: total,
      marks: { status: 'draft', entered, missing: total - entered, total, submittedAt: null, publishedAt: null },
      ...summaryOverrides,
    }),
    roster,
    rows,
    leader,
    announcements,
  }
  recomputeMarksStatus(fixture)
  seedLecturerModule(fixture)
  return fixture
}
