import { http, HttpResponse } from 'msw'

import type {
  AnnouncementView,
  GradeResult,
  ModuleSummary,
  MyEnrolment,
  PublicationBrief,
  TimetableEntry,
  WindowInfo,
} from '@/api/types/common'
import type {
  CompletedModule,
  DashboardModule,
  DashboardResponse,
  ResultsResponse,
  ResultsSemester,
} from '@/api/types/student'

import { hasCsrf, problem } from '../http'

import {
  AUTUMN_CLOSES,
  AUTUMN_OPENS,
  AUTUMN_WITHDRAWAL,
  SPRING_CLOSES,
  SPRING_WITHDRAWAL,
} from './modules'

/**
 * Factories and MSW handlers for the student routes (02-api.md section 8.3, `/api/me`). They replace
 * the `*Shape` placeholders of test/factories.ts for stage S7's tests; instants are written as the
 * API writes them (UTC, three fractional digits).
 */

export const CURRENT_YEAR = '2026/27'
export const LAST_YEAR = '2025/26'

let sequence = 0
const nextId = () => {
  sequence += 1
  return `00000000-0000-7000-9000-${String(sequence).padStart(12, '0')}`
}

export function makeStudentWindow(overrides: Partial<WindowInfo> = {}): WindowInfo {
  const semester = overrides.semester ?? 'autumn'
  return {
    id: nextId(),
    academicYear: CURRENT_YEAR,
    semester,
    opensAt: AUTUMN_OPENS,
    closesAt: semester === 'autumn' ? AUTUMN_CLOSES : SPRING_CLOSES,
    withdrawalDeadlineAt: semester === 'autumn' ? AUTUMN_WITHDRAWAL : SPRING_WITHDRAWAL,
    state: 'open',
    ...overrides,
  }
}

export function makeMyEnrolment(overrides: Partial<MyEnrolment> = {}): MyEnrolment {
  return {
    moduleCode: 'CS3001',
    title: 'Distributed Systems',
    credits: 15,
    semester: 'autumn',
    academicYear: CURRENT_YEAR,
    status: 'active',
    enrolledAt: '2026-09-14T10:00:00.000Z',
    withdrawnAt: null,
    canWithdraw: true,
    withdrawBlockedReason: null,
    withdrawalDeadlineAt: AUTUMN_WITHDRAWAL,
    ...overrides,
  }
}

export function makeGrade(overrides: Partial<GradeResult> = {}): GradeResult {
  return {
    moduleCode: 'CS2001',
    moduleTitle: 'Algorithms and Data Structures',
    credits: 15,
    semester: 'autumn',
    academicYear: LAST_YEAR,
    outcome: 'mark',
    mark: 68,
    publishedAt: '2026-01-26T09:00:00.000Z',
    correctedAt: null,
    ...overrides,
  }
}

export function makeTimetableSlot(overrides: Partial<TimetableEntry> = {}): TimetableEntry {
  return {
    moduleCode: 'CS3001',
    moduleTitle: 'Distributed Systems',
    semester: 'autumn',
    day: 'monday',
    startTime: '09:00',
    endTime: '11:00',
    room: 'B-201',
    kind: 'lecture',
    ...overrides,
  }
}

export function makeStudentAnnouncement(
  overrides: Partial<AnnouncementView> = {},
): AnnouncementView {
  return {
    id: nextId(),
    scope: 'university',
    moduleCode: null,
    title: 'Autumn 2025/26 results are available',
    body: 'Sign in to see your marks.',
    pinned: true,
    publishedAt: '2026-09-28T09:00:00.000Z',
    expiresAt: null,
    author: 'Registry',
    createdAt: '2026-09-27T12:00:00.000Z',
    updatedAt: '2026-09-27T12:00:00.000Z',
    ...overrides,
  }
}

export function makePublication(overrides: Partial<PublicationBrief> = {}): PublicationBrief {
  return {
    academicYear: LAST_YEAR,
    semester: 'autumn',
    publishAt: '2026-09-28T09:00:00.000Z',
    state: 'live',
    ...overrides,
  }
}

export function makeDashboardModule(overrides: Partial<DashboardModule> = {}): DashboardModule {
  return {
    code: 'CS3001',
    title: 'Distributed Systems',
    credits: 15,
    semester: 'autumn',
    academicYear: CURRENT_YEAR,
    enrolledAt: '2026-09-14T10:00:00.000Z',
    canWithdraw: true,
    withdrawBlockedReason: null,
    withdrawalDeadlineAt: AUTUMN_WITHDRAWAL,
    ...overrides,
  }
}

export function makeCompleted(overrides: Partial<CompletedModule> = {}): CompletedModule {
  return {
    code: 'CS2001',
    title: 'Algorithms and Data Structures',
    credits: 15,
    semester: 'autumn',
    academicYear: LAST_YEAR,
    outcome: 'mark',
    mark: 68,
    band: 'Upper Second (2:1)',
    ...overrides,
  }
}

/** `GET /api/me/dashboard` for S000001 on the demo: CS3001 this year, CS2001 completed with 68. */
export function makeDashboard(overrides: Partial<DashboardResponse> = {}): DashboardResponse {
  return {
    studentNumber: 'S000001',
    fullName: 'Aisha Khan',
    programme: 'BSc Computer Science',
    yearOfStudy: 2,
    academicYear: CURRENT_YEAR,
    currentSemester: 'autumn',
    modules: [makeDashboardModule()],
    completed: [makeCompleted()],
    timetable: [makeTimetableSlot()],
    results: [makeGrade()],
    weightedAverage: 68,
    classification: 'Upper Second (2:1)',
    credits: { autumn: 15, spring: 0, limit: 60 },
    nextPublication: null,
    latestPublication: makePublication(),
    enrolmentWindows: [makeStudentWindow(), makeStudentWindow({ semester: 'spring' })],
    announcements: [makeStudentAnnouncement()],
    ...overrides,
  }
}

export function makeResultsSemester(overrides: Partial<ResultsSemester> = {}): ResultsSemester {
  return {
    academicYear: LAST_YEAR,
    semester: 'autumn',
    state: 'published',
    publishAt: null,
    results: [makeGrade()],
    ...overrides,
  }
}

export function makeResults(overrides: Partial<ResultsResponse> = {}): ResultsResponse {
  return {
    semesters: [makeResultsSemester()],
    weightedAverage: 68,
    classification: 'Upper Second (2:1)',
    ...overrides,
  }
}

// -------------------------------------------------------------------------------------------------
// Handlers

export const dashboardHandler = (dashboard: DashboardResponse | (() => DashboardResponse)) =>
  http.get('/api/me/dashboard', () =>
    HttpResponse.json(typeof dashboard === 'function' ? dashboard() : dashboard),
  )

export const resultsHandler = (results: ResultsResponse | (() => ResultsResponse)) =>
  http.get('/api/me/results', () =>
    HttpResponse.json(typeof results === 'function' ? results() : results),
  )

export const timetableHandler = (entries: TimetableEntry[]) =>
  http.get('/api/me/timetable', () => HttpResponse.json(entries))

export const enrolmentsHandler = (rows: MyEnrolment[] | (() => MyEnrolment[])) =>
  http.get('/api/me/enrolments', () =>
    HttpResponse.json(typeof rows === 'function' ? rows() : rows),
  )

/**
 * A small stateful stand-in for enrol and withdraw over a catalogue and the student's rows, with the
 * server's rules in the order it checks them (window, capacity, credits). Returns the handlers and
 * the state, so a test can inspect what the "server" holds.
 */
export function createEnrolmentServer({
  modules,
  rows = [],
  academicYear = CURRENT_YEAR,
}: {
  modules: ModuleSummary[]
  rows?: MyEnrolment[]
  academicYear?: string
}) {
  const state = {
    modules: modules.map((module) => ({ ...module })),
    rows: rows.map((row) => ({ ...row })),
    requests: { enrol: 0, withdraw: 0 },
  }

  const handlers = [
    http.get('/api/modules', () => HttpResponse.json(state.modules)),
    http.get('/api/me/enrolments', () => HttpResponse.json(state.rows)),
    http.post('/api/me/enrolments', async ({ request }) => {
      state.requests.enrol += 1
      if (!hasCsrf(request)) return problem('antiforgery')
      const { moduleCode } = (await request.json()) as { moduleCode: string }
      const module = state.modules.find((item) => item.code === moduleCode.toUpperCase())
      if (!module) return problem('module-not-found')
      const existing = state.rows.find((row) => row.moduleCode === module.code)
      if (existing?.status === 'active' && existing.academicYear === academicYear) {
        return problem('already-enrolled')
      }
      if (!module.isActive) return problem('module-inactive')
      if (module.enrolmentState !== 'open') {
        return problem('enrolment-window-closed', {
          extensions: {
            semester: module.semester,
            opensAt: module.windowOpensAt,
            closesAt: module.windowClosesAt,
          },
        })
      }
      const used = state.rows
        .filter(
          (row) =>
            row.status === 'active' &&
            row.academicYear === academicYear &&
            row.semester === module.semester,
        )
        .reduce((sum, row) => sum + row.credits, 0)
      if (used + module.credits > 60) {
        return problem('credit-limit-exceeded', {
          extensions: {
            currentCredits: used,
            moduleCredits: module.credits,
            limit: 60,
            semester: module.semester,
          },
        })
      }
      if (module.enrolledCount >= module.capacity) return problem('module-full')
      module.enrolledCount += 1
      module.placesRemaining = Math.max(0, module.capacity - module.enrolledCount)
      const enrolledAt = '2026-09-29T10:00:00.000Z'
      state.rows = [
        {
          moduleCode: module.code,
          title: module.title,
          credits: module.credits,
          semester: module.semester,
          academicYear,
          status: 'active',
          enrolledAt,
          withdrawnAt: null,
          canWithdraw: true,
          withdrawBlockedReason: null,
          withdrawalDeadlineAt: module.withdrawalDeadlineAt,
        },
        ...state.rows.filter((row) => row.moduleCode !== module.code),
      ]
      return HttpResponse.json(
        { moduleCode: module.code, enrolledAt, placesRemaining: module.placesRemaining },
        { status: 201 },
      )
    }),
    http.delete('/api/me/enrolments/:code', ({ request, params }) => {
      state.requests.withdraw += 1
      if (!hasCsrf(request)) return problem('antiforgery')
      const code = String(params.code)
      const row = state.rows.find(
        (item) =>
          item.moduleCode === code &&
          item.status === 'active' &&
          item.academicYear === academicYear,
      )
      if (!row) return problem('not-enrolled')
      if (!row.canWithdraw) {
        return row.withdrawBlockedReason === 'results'
          ? problem('results-exist')
          : problem('withdrawal-deadline-passed', {
              extensions: { withdrawalDeadlineAt: row.withdrawalDeadlineAt },
            })
      }
      row.status = 'withdrawn'
      row.withdrawnAt = '2026-09-29T10:05:00.000Z'
      row.canWithdraw = false
      const module = state.modules.find((item) => item.code === code)
      if (module) {
        module.enrolledCount = Math.max(0, module.enrolledCount - 1)
        module.placesRemaining = Math.max(0, module.capacity - module.enrolledCount)
      }
      return new HttpResponse(null, { status: 204 })
    }),
  ]

  return { state, handlers }
}
