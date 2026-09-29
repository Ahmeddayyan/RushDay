import type { Semester } from './types/common'

/**
 * Query keys (05-frontend.md section 6.2), written once in stage S5: every area reads its keys from
 * here and never spells a key inline, so an invalidation in one stage always matches the query of
 * another. `queryTimings` holds the per-key freshness rules of the same table.
 */

/** List filters that live in the URL; part of the key so each filter combination caches separately. */
export type ListParams = Readonly<Record<string, string | number | boolean | null | undefined>>

export const queryKeys = {
  /** GET /api */
  index: ['index'] as const,
  /** GET /api/public/status */
  publicStatus: ['public', 'status'] as const,
  /** GET /api/auth/me */
  authMe: ['auth', 'me'] as const,

  student: {
    all: ['student'] as const,
    /** GET /api/me/dashboard */
    dashboard: ['student', 'dashboard'] as const,
    /** GET /api/me/results */
    results: ['student', 'results'] as const,
    /** GET /api/me/timetable */
    timetable: ['student', 'timetable'] as const,
    /** GET /api/me/enrolments */
    enrolments: ['student', 'enrolments'] as const,
  },

  modules: {
    all: ['modules'] as const,
    /** GET /api/modules */
    catalogue: ['modules', 'catalogue'] as const,
    /** GET /api/modules/{code} */
    detail: (code: string) => ['modules', code] as const,
  },

  /** GET /api/announcements */
  announcements: ['announcements'] as const,

  lecturer: {
    all: ['lecturer'] as const,
    /** GET /api/lecturer/modules */
    modules: ['lecturer', 'modules'] as const,
    /** Prefix of every key of one module, for invalidation. */
    module: (code: string) => ['lecturer', 'module', code] as const,
    /** GET /api/lecturer/modules/{code}/roster */
    roster: (code: string, params: { q: string; page: number; pageSize: number }) =>
      ['lecturer', 'module', code, 'roster', params] as const,
    /** GET /api/lecturer/modules/{code}/marks */
    marks: (code: string, params: { q: string; page: number }) =>
      ['lecturer', 'module', code, 'marks', params] as const,
    /** GET /api/lecturer/modules/{code}/announcements */
    moduleAnnouncements: (code: string) => ['lecturer', 'module', code, 'announcements'] as const,
  },

  admin: {
    all: ['admin'] as const,
    overview: ['admin', 'overview'] as const,
    settings: ['admin', 'settings'] as const,
    windows: ['admin', 'windows'] as const,
    results: (academicYear: string, semester: Semester) =>
      ['admin', 'results', academicYear, semester] as const,
    lecturers: (q: string) => ['admin', 'lecturers', q] as const,
    students: (params: ListParams) => ['admin', 'students', params] as const,
    accounts: (params: ListParams) => ['admin', 'accounts', params] as const,
    audit: (params: ListParams) => ['admin', 'audit', params] as const,
    student: (studentNumber: string) => ['admin', 'student', studentNumber] as const,
    modules: (includeInactive: boolean) => ['admin', 'modules', includeInactive] as const,
    announcements: ['admin', 'announcements'] as const,
    /** Prefix of every key of one module, for invalidation. */
    module: (code: string) => ['admin', 'module', code] as const,
    moduleRoster: (code: string, params: ListParams) =>
      ['admin', 'module', code, 'roster', params] as const,
    moduleMarks: (code: string, params: ListParams) =>
      ['admin', 'module', code, 'marks', params] as const,
    opsMetrics: ['admin', 'ops', 'metrics'] as const,
  },

  /** GET /data/load-results.json (static file) */
  loadResults: ['load-results'] as const,
}

/** Freshness per key (05-frontend.md section 6.2); anything not listed uses the 30 s default. */
export const queryTimings = {
  index: { staleTime: Infinity },
  publicStatus: { staleTime: 60_000 },
  studentDashboard: { staleTime: 60_000 },
  studentEnrolments: { staleTime: 15_000 },
  catalogue: { staleTime: 15_000 },
  /** Plus `refetchInterval` while enrolment for the module's semester is open and the page is visible. */
  moduleDetail: { staleTime: 5_000, refetchInterval: 10_000 },
  announcements: { staleTime: 60_000 },
  adminOverview: { refetchInterval: 30_000 },
  opsMetrics: { refetchInterval: 5_000, refetchIntervalInBackground: false },
  loadResults: { staleTime: Infinity },
} as const
