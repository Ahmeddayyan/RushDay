import { http, HttpResponse } from 'msw'

import type { Lecturer, ModuleDetail, ModuleSummary, TimetableEntry } from '@/api/types/common'

import { problem } from '../http'

/**
 * Factories and MSW handlers for the module routes (02-api.md section 8.2): `GET /api/modules` and
 * `GET /api/modules/{code}`. Instants are written as the API writes them: UTC with exactly three
 * fractional digits.
 */

export const AUTUMN_OPENS = '2026-09-14T09:00:00.000Z'
export const AUTUMN_CLOSES = '2026-10-02T17:00:00.000Z'
export const AUTUMN_WITHDRAWAL = '2026-10-30T17:00:00.000Z'
export const SPRING_CLOSES = '2027-01-29T17:00:00.000Z'
export const SPRING_WITHDRAWAL = '2027-02-26T17:00:00.000Z'

export function makeLecturer(overrides: Partial<Lecturer> = {}): Lecturer {
  return {
    staffNumber: 'L00001',
    fullName: 'Grace Hopper',
    title: 'Dr',
    role: 'leader',
    left: false,
    ...overrides,
  }
}

/** A catalogue row; CS3099 with 12 of 30 places left, autumn window open, by default. */
export function makeModule(overrides: Partial<ModuleSummary> = {}): ModuleSummary {
  const code = overrides.code ?? 'CS3099'
  const capacity = overrides.capacity ?? 30
  const enrolledCount = overrides.enrolledCount ?? 18
  const semester = overrides.semester ?? 'autumn'
  return {
    code,
    title: 'Software Engineering Project',
    department: code.slice(0, 2),
    level: Number(code[2] ?? '1') as 1 | 2 | 3,
    credits: 15,
    semester,
    capacity,
    enrolledCount,
    placesRemaining: Math.max(0, capacity - enrolledCount),
    isActive: true,
    lecturers: [makeLecturer()],
    enrolmentState: 'open',
    windowOpensAt: AUTUMN_OPENS,
    windowClosesAt: semester === 'autumn' ? AUTUMN_CLOSES : SPRING_CLOSES,
    withdrawalDeadlineAt: semester === 'autumn' ? AUTUMN_WITHDRAWAL : SPRING_WITHDRAWAL,
    ...overrides,
  }
}

export function makeSlot(overrides: Partial<TimetableEntry> = {}): TimetableEntry {
  return {
    moduleCode: 'CS3099',
    moduleTitle: 'Software Engineering Project',
    semester: 'autumn',
    day: 'tuesday',
    startTime: '10:00',
    endTime: '12:00',
    room: 'C-104',
    kind: 'lecture',
    ...overrides,
  }
}

/** `GET /api/modules/{code}`: the summary plus description and timetable. */
export function makeModuleDetail(overrides: Partial<ModuleDetail> = {}): ModuleDetail {
  const summary = makeModule(overrides)
  return {
    ...summary,
    description:
      'Work in a team of five on a real brief from a local organisation.\nAssessed by portfolio.',
    timetable: [
      makeSlot({ moduleCode: summary.code, moduleTitle: summary.title }),
      makeSlot({
        moduleCode: summary.code,
        moduleTitle: summary.title,
        day: 'thursday',
        startTime: '14:00',
        endTime: '16:00',
        room: 'C-Lab2',
        kind: 'lab',
      }),
    ],
    ...overrides,
  }
}

/** A small catalogue across departments, levels and semesters. */
export function makeCatalogue(): ModuleSummary[] {
  return [
    makeModule({
      code: 'CS1001',
      title: 'Programming Fundamentals',
      enrolledCount: 100,
      capacity: 120,
    }),
    makeModule({ code: 'CS3001', title: 'Distributed Systems', enrolledCount: 100, capacity: 100 }),
    makeModule(),
    makeModule({ code: 'MA2004', title: 'Linear Algebra', semester: 'spring', enrolledCount: 10 }),
    makeModule({ code: 'PH1002', title: 'Mechanics', enrolledCount: 0 }),
    makeModule({ code: 'EE2010', title: 'Signals and Systems', semester: 'spring', credits: 30 }),
  ]
}

export function catalogueHandler(modules: ModuleSummary[] | (() => ModuleSummary[])) {
  return http.get('/api/modules', () =>
    HttpResponse.json(typeof modules === 'function' ? modules() : modules),
  )
}

/** Answers `GET /api/modules/{code}` from `details` by code; anything else is 404 `module-not-found`. */
export function moduleHandler(
  details: ModuleDetail[] | ((code: string) => ModuleDetail | undefined),
) {
  return http.get('/api/modules/:code', ({ params }) => {
    const code = String(params.code)
    const detail =
      typeof details === 'function' ? details(code) : details.find((item) => item.code === code)
    return detail ? HttpResponse.json(detail) : problem('module-not-found')
  })
}
