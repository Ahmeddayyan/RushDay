import type {
  AccountView,
  AnnouncementView,
  AuditEventView,
  GradeResult,
  Me,
  ModuleSummary,
  PublicationBrief,
  Semester,
  TimetableEntry,
  WindowInfo,
} from '@/api/types/common'
import type { ApiIndex, PublicStatus } from '@/api/types/public'

/**
 * Test data builders (05-frontend.md section 13.1). Each returns a complete, valid object of the
 * API shape with sensible defaults; pass overrides for what the test is about.
 *
 * `DashboardResponse`, `MarksSheet` and `OpsSnapshot` are area types owned by stages S7, S8 and S10
 * (`api/types/{student,lecturer,ops}.ts`), which do not exist yet when this file is written; the
 * `*Shape` interfaces below mirror 02-api.md sections 8.3 and 8.4 and 04-performance-and-ops.md
 * section 6.3 field for field, so the builders are structurally assignable to those types.
 */

let sequence = 0
const nextId = () => {
  sequence += 1
  return `00000000-0000-7000-8000-${String(sequence).padStart(12, '0')}`
}

// -------------------------------------------------------------------------------------------------
// Session

export function makeMe(overrides: Partial<Me> = {}): Me {
  return {
    id: nextId(),
    username: 'S000001',
    displayName: 'Aisha Khan',
    role: 'Student',
    studentNumber: 'S000001',
    staffNumber: null,
    mustChangePassword: false,
    mfaEnabled: false,
    mfaSetupRequired: false,
    isDemo: false,
    csrfToken: 'csrf-signed-in',
    ...overrides,
  }
}

export const makeStudentMe = (overrides: Partial<Me> = {}) => makeMe(overrides)

export const makeLecturerMe = (overrides: Partial<Me> = {}) =>
  makeMe({
    username: 'L00001',
    displayName: 'Dr Grace Hopper',
    role: 'Lecturer',
    studentNumber: null,
    staffNumber: 'L00001',
    ...overrides,
  })

export const makeAdminMe = (overrides: Partial<Me> = {}) =>
  makeMe({
    username: 'registry.admin',
    displayName: 'Registry Administrator',
    role: 'Admin',
    studentNumber: null,
    staffNumber: null,
    mfaEnabled: true,
    ...overrides,
  })

// -------------------------------------------------------------------------------------------------
// Public

export function makeWindow(overrides: Partial<WindowInfo> = {}): WindowInfo {
  return {
    id: nextId(),
    academicYear: '2026/27',
    semester: 'autumn',
    opensAt: '2026-09-14T09:00:00Z',
    closesAt: '2026-10-02T17:00:00Z',
    withdrawalDeadlineAt: '2026-10-30T17:00:00Z',
    state: 'open',
    ...overrides,
  }
}

export function makePublicationBrief(overrides: Partial<PublicationBrief> = {}): PublicationBrief {
  return {
    academicYear: '2025/26',
    semester: 'autumn',
    publishAt: '2026-09-28T09:00:00Z',
    state: 'live',
    ...overrides,
  }
}

export const DEMO_ACCOUNTS: NonNullable<PublicStatus['demo']>['accounts'] = [
  {
    role: 'Student',
    username: 'S000001',
    password: 'Student-Demo-2026!',
    hint: 'Completed Autumn 2025/26 with marks published; enrolled on CS3001 for Autumn 2026/27; can enrol on CS3099',
  },
  {
    role: 'Lecturer',
    username: 'L00001',
    password: 'Lecturer-Demo-2026!',
    hint: 'Leads CS3001 (100 students, marks in draft) and CS3099 (30 places)',
  },
  {
    role: 'Admin',
    username: 'admin',
    password: 'Admin-Demo-2026!',
    hint: 'Windows, results publication and corrections, accounts, audit, operations',
  },
]

export function makePublicStatus(overrides: Partial<PublicStatus> = {}): PublicStatus {
  return {
    serverTime: new Date().toISOString(),
    institution: {
      name: 'Northbridge University',
      shortName: 'Northbridge',
      timeZone: 'Europe/London',
      privacyNoticeUrl: null,
      resultsFootnote:
        'Below 40? Your personal tutor or the academic office can explain resit options.',
      support: null,
    },
    academicYear: '2026/27',
    currentSemester: 'autumn',
    nextPublication: null,
    latestPublication: makePublicationBrief(),
    enrolmentWindows: [
      makeWindow(),
      makeWindow({ semester: 'spring', closesAt: '2027-01-29T17:00:00Z' }),
    ],
    demo: null,
    ...overrides,
  }
}

export const makeDemoStatus = (overrides: Partial<PublicStatus> = {}) =>
  makePublicStatus({ demo: { accounts: DEMO_ACCOUNTS }, ...overrides })

export function makeApiIndex(overrides: Partial<ApiIndex> = {}): ApiIndex {
  return {
    name: 'RushDay',
    story:
      "I built RushDay because my own university's portal fell over every results day and enrolment window. I wanted to understand why that happens and to build a portal that doesn't crash under the same load.",
    commit: '37c150c0ffee',
    environment: 'Test',
    links: {
      health: '/api/health/live',
      ready: '/api/health/ready',
      status: '/api/public/status',
      login: '/api/auth/login',
      github: 'https://github.com/Ahmeddayyan/RushDay',
    },
    ...overrides,
  }
}

// -------------------------------------------------------------------------------------------------
// Shared shapes

export function makeModuleSummary(overrides: Partial<ModuleSummary> = {}): ModuleSummary {
  return {
    code: 'CS3099',
    title: 'Software Engineering Project',
    department: 'CS',
    level: 3,
    credits: 15,
    semester: 'autumn',
    capacity: 30,
    enrolledCount: 18,
    placesRemaining: 12,
    isActive: true,
    lecturers: [
      { staffNumber: 'L00001', fullName: 'Grace Hopper', title: 'Dr', role: 'leader', left: false },
    ],
    enrolmentState: 'open',
    windowOpensAt: '2026-09-14T09:00:00Z',
    windowClosesAt: '2026-10-02T17:00:00Z',
    withdrawalDeadlineAt: '2026-10-30T17:00:00Z',
    ...overrides,
  }
}

export function makeAnnouncement(overrides: Partial<AnnouncementView> = {}): AnnouncementView {
  return {
    id: nextId(),
    scope: 'university',
    moduleCode: null,
    title: 'Autumn 2025/26 results are available',
    body: 'Sign in to see your marks.\n\nBelow 40? Your personal tutor or the academic office can explain resit options.',
    pinned: false,
    publishedAt: '2026-09-28T09:00:00Z',
    expiresAt: null,
    author: 'Registry',
    createdAt: '2026-09-27T12:00:00Z',
    updatedAt: '2026-09-27T12:00:00Z',
    ...overrides,
  }
}

export function makeAccountView(overrides: Partial<AccountView> = {}): AccountView {
  return {
    id: nextId(),
    username: 'S000002',
    displayName: 'Ben Carter',
    role: 'Student',
    studentNumber: 'S000002',
    staffNumber: null,
    email: 's000002@students.example.ac.uk',
    state: 'active',
    lockoutEnd: null,
    mustChangePassword: false,
    mfaEnabled: false,
    isDemo: false,
    createdAt: '2026-09-01T09:00:00Z',
    lastLoginAt: '2026-09-27T08:15:00Z',
    ...overrides,
  }
}

export function makeAuditEvent(overrides: Partial<AuditEventView> = {}): AuditEventView {
  return {
    id: nextId(),
    occurredAt: '2026-09-28T09:00:00Z',
    actorUsername: 'registry.admin',
    actorRole: 'Admin',
    action: 'results.published',
    subjectType: 'Publication',
    subjectId: nextId(),
    studentNumber: null,
    moduleCode: null,
    details: { modules: 60, grades: 80000 },
    requestId: '00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01',
    ...overrides,
  }
}

export function makeGradeResult(overrides: Partial<GradeResult> = {}): GradeResult {
  return {
    moduleCode: 'CS2001',
    moduleTitle: 'Algorithms and Data Structures',
    credits: 15,
    semester: 'autumn',
    academicYear: '2025/26',
    outcome: 'mark',
    mark: 68,
    publishedAt: '2026-01-26T09:00:00Z',
    correctedAt: null,
    ...overrides,
  }
}

export function makeTimetableEntry(overrides: Partial<TimetableEntry> = {}): TimetableEntry {
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

// -------------------------------------------------------------------------------------------------
// Area response shapes (see the note at the top)

/** Mirrors `DashboardResponse` of 02-api.md section 8.3. */
export interface DashboardResponseShape {
  studentNumber: string
  fullName: string
  programme: string
  yearOfStudy: number
  academicYear: string
  currentSemester: Semester
  modules: {
    code: string
    title: string
    credits: number
    semester: Semester
    academicYear: string
    enrolledAt: string
    canWithdraw: boolean
    withdrawBlockedReason: 'deadline' | 'results' | 'year' | null
    withdrawalDeadlineAt: string | null
  }[]
  completed: {
    code: string
    title: string
    credits: number
    semester: Semester
    academicYear: string
    outcome: 'mark' | 'absent' | 'deferred' | null
    mark: number | null
    band: string | null
  }[]
  timetable: TimetableEntry[]
  results: GradeResult[]
  weightedAverage: number | null
  classification: string | null
  credits: { autumn: number; spring: number; limit: 60 }
  nextPublication: PublicationBrief | null
  latestPublication: PublicationBrief | null
  enrolmentWindows: WindowInfo[]
  announcements: AnnouncementView[]
}

export function makeDashboard(
  overrides: Partial<DashboardResponseShape> = {},
): DashboardResponseShape {
  return {
    studentNumber: 'S000001',
    fullName: 'Aisha Khan',
    programme: 'BSc Computer Science',
    yearOfStudy: 3,
    academicYear: '2026/27',
    currentSemester: 'autumn',
    modules: [
      {
        code: 'CS3001',
        title: 'Distributed Systems',
        credits: 15,
        semester: 'autumn',
        academicYear: '2026/27',
        enrolledAt: '2026-09-14T10:00:00Z',
        canWithdraw: true,
        withdrawBlockedReason: null,
        withdrawalDeadlineAt: '2026-10-30T17:00:00Z',
      },
    ],
    completed: [
      {
        code: 'CS2001',
        title: 'Algorithms and Data Structures',
        credits: 15,
        semester: 'autumn',
        academicYear: '2025/26',
        outcome: 'mark',
        mark: 68,
        band: '2:1',
      },
    ],
    timetable: [makeTimetableEntry()],
    results: [makeGradeResult()],
    weightedAverage: 68,
    classification: '2:1',
    credits: { autumn: 15, spring: 0, limit: 60 },
    nextPublication: null,
    latestPublication: makePublicationBrief(),
    enrolmentWindows: [makeWindow()],
    announcements: [makeAnnouncement({ pinned: true })],
    ...overrides,
  }
}

/** Mirrors `MarksSheet` of 02-api.md section 8.4. */
export interface MarksSheetShape {
  code: string
  title: string
  academicYear: string
  status: 'noStudents' | 'draft' | 'submitted' | 'scheduled' | 'published'
  submittedAt: string | null
  publishedAt: string | null
  myRole: 'leader' | 'teacher' | null
  leader: string | null
  summary: { entered: number; missing: number; total: number }
  rows: {
    studentNumber: string
    fullName: string
    enrolmentStatus: 'active' | 'withdrawn'
    outcome: 'mark' | 'absent' | 'deferred' | null
    mark: number | null
    gradeStatus: 'draft' | 'submitted' | 'published' | null
    version: number | null
    updatedAt: string | null
    enteredBy: string | null
    correctedAt: string | null
  }[]
  page: number
  pageSize: number
  total: number
}

export type MarksRowShape = MarksSheetShape['rows'][number]

export function makeMarksRow(index: number, overrides: Partial<MarksRowShape> = {}): MarksRowShape {
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

export function makeMarksSheet(
  overrides: Partial<MarksSheetShape> = {},
  rowCount = 3,
): MarksSheetShape {
  const rows = Array.from({ length: rowCount }, (_, index) => makeMarksRow(index))
  return {
    code: 'CS3001',
    title: 'Distributed Systems',
    academicYear: '2026/27',
    status: 'draft',
    submittedAt: null,
    publishedAt: null,
    myRole: 'leader',
    leader: 'Dr Grace Hopper',
    summary: { entered: 0, missing: rowCount, total: rowCount },
    rows,
    page: 1,
    pageSize: 100,
    total: rowCount,
    ...overrides,
  }
}

/** Mirrors `OpsSnapshot` of 04-performance-and-ops.md section 6.3. */
export interface OpsSnapshotShape {
  sampledAt: string
  startedAt: string
  uptimeSeconds: number
  commit: string
  environment: string
  runtime: {
    dotnetVersion: string
    gcMode: 'workstation' | 'server'
    maxPoolSize: number
    rateLimiting: {
      maxConcurrent: number
      maxQueued: number
      loginPerUserPerMinute: number
      enrolPerUserPer10s: number
      writePerUserPerMinute: number
    }
  }
  process: { workingSetBytes: number; gcHeapBytes: number; threadPoolThreads: number }
  http: {
    inFlight: number
    last60s: {
      requests: number
      perSecond: number
      p50Ms: number
      p95Ms: number
      p99Ms: number
      status2xx: number
      status4xx: number
      status5xx: number
      rateLimited429: number
      shed503: number
    }
  }
  db: {
    poolMax: number
    poolBusy: number
    poolIdle: number
    pendingRequests: number
    waitTimeoutsTotal: number
  }
  cache: { name: string; hits: number; misses: number }[]
  enrolment: {
    accepted: number
    rejected: {
      moduleFull: number
      alreadyEnrolled: number
      windowClosed: number
      creditLimit: number
      resultsExist: number
      other: number
    }
    p95Ms: number
  }
  dashboard: { p95Ms: number; queriesPerRequest: number }
  auth: { loginsSucceeded: number; loginsFailed: number; lockouts: number }
  series: {
    minute: string
    requests: number
    p95Ms: number
    p99Ms: number
    status5xx: number
    shed503: number
    rateLimited429: number
  }[]
  backfills: { name: string; completedAt: string; rowsAffected: number; notes: string | null }[]
  dataQuality: {
    refreshedAt: string | null
    staleSince: string | null
    modulesOverCapacity: { code: string; capacity: number; enrolledCount: number }[]
    enrolledCountDrift: number
  }
}

export function makeOpsSnapshot(overrides: Partial<OpsSnapshotShape> = {}): OpsSnapshotShape {
  const sampledAt = '2026-09-28T09:00:00Z'
  return {
    sampledAt,
    startedAt: '2026-09-28T08:00:00Z',
    uptimeSeconds: 3600,
    commit: '37c150c',
    environment: 'Production',
    runtime: {
      dotnetVersion: '10.0.0',
      gcMode: 'workstation',
      maxPoolSize: 20,
      rateLimiting: {
        maxConcurrent: 24,
        maxQueued: 96,
        loginPerUserPerMinute: 10,
        enrolPerUserPer10s: 5,
        writePerUserPerMinute: 120,
      },
    },
    process: { workingSetBytes: 180_000_000, gcHeapBytes: 60_000_000, threadPoolThreads: 12 },
    http: {
      inFlight: 3,
      last60s: {
        requests: 1200,
        perSecond: 20,
        p50Ms: 8,
        p95Ms: 42,
        p99Ms: 110,
        status2xx: 1180,
        status4xx: 20,
        status5xx: 0,
        rateLimited429: 0,
        shed503: 0,
      },
    },
    db: { poolMax: 20, poolBusy: 2, poolIdle: 4, pendingRequests: 0, waitTimeoutsTotal: 0 },
    cache: [{ name: 'catalogue:all', hits: 950, misses: 12 }],
    enrolment: {
      accepted: 30,
      rejected: {
        moduleFull: 470,
        alreadyEnrolled: 0,
        windowClosed: 0,
        creditLimit: 0,
        resultsExist: 0,
        other: 0,
      },
      p95Ms: 21,
    },
    dashboard: { p95Ms: 35, queriesPerRequest: 5 },
    auth: { loginsSucceeded: 800, loginsFailed: 12, lockouts: 0 },
    series: Array.from({ length: 60 }, (_, index) => ({
      minute: new Date(Date.parse(sampledAt) - (59 - index) * 60_000).toISOString(),
      requests: 1000 + index,
      p95Ms: 40,
      p99Ms: 100,
      status5xx: 0,
      shed503: 0,
      rateLimited429: 0,
    })),
    backfills: [
      {
        name: 'demo_reset_hot_module',
        completedAt: '2026-09-28T08:00:05Z',
        rowsAffected: 154,
        notes: null,
      },
    ],
    dataQuality: {
      refreshedAt: sampledAt,
      staleSince: null,
      modulesOverCapacity: [],
      enrolledCountDrift: 0,
    },
    ...overrides,
  }
}
