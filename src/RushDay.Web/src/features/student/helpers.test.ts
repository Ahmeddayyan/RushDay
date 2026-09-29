import { describe, expect, it } from 'vitest'

import { makeModule } from '@/test/handlers/modules'
import { makeGrade, makeMyEnrolment, makeTimetableSlot } from '@/test/handlers/student'

import { filterModules, hasFilters, readFilters } from './catalogueFilters'
import { creditBudgetText, publicationSentence, windowClosedSentence } from './copy'
import { creditsUsed, deriveEnrolState, enrolledThisYear, isCurrentYearRow } from './enrolState'
import { completedResultText, markBand, shortBand, weightedAverage } from './grades'
import { greeting, minutesOf, zonedMoment } from './time'
import { nextClasses, placeDay } from './timetable'

describe('grades', () => {
  it('bands marks as Classification.cs does', () => {
    expect([70, 69, 60, 59, 50, 49, 40, 39, 0, 100].map(markBand)).toEqual([
      'First',
      '2:1',
      '2:1',
      '2:2',
      '2:2',
      'Third',
      'Third',
      'Fail',
      'Fail',
      'First',
    ])
    expect(shortBand('Upper Second (2:1)', 65)).toBe('2:1')
    expect(shortBand('Something new', 45)).toBe('Third')
    expect(shortBand(null, null)).toBeNull()
  })

  it('weights marks by credits and leaves absences and deferrals out (80×30 + 50×15 → 70.0)', () => {
    const results = [
      makeGrade({ mark: 80, credits: 30 }),
      makeGrade({ moduleCode: 'CS2002', mark: 50, credits: 15 }),
      makeGrade({ moduleCode: 'CS2003', outcome: 'absent', mark: null, credits: 30 }),
      makeGrade({ moduleCode: 'CS2004', outcome: 'deferred', mark: null, credits: 15 }),
    ]
    expect(weightedAverage(results)).toBe(70)
    expect(weightedAverage([makeGrade({ outcome: 'absent', mark: null })])).toBeNull()
  })

  it('words a completed module result', () => {
    expect(completedResultText({ outcome: 'mark', mark: 68, band: 'Upper Second (2:1)' })).toBe(
      'Mark 68 (2:1)',
    )
    expect(completedResultText({ outcome: 'absent', mark: null, band: null })).toBe('Absent')
    expect(completedResultText({ outcome: 'deferred', mark: null, band: null })).toBe('Deferred')
    expect(completedResultText({ outcome: null, mark: null, band: null })).toBe(
      'Result not yet published',
    )
  })
})

describe('enrolment state', () => {
  const base = { currentYear: '2026/27', creditsUsed: 0, enrolmentsFresh: true }

  it('derives every state of the EnrolButton table', () => {
    const module = makeModule()
    const kind = (overrides: Parameters<typeof deriveEnrolState>[0]) =>
      deriveEnrolState(overrides).kind

    expect(kind({ ...base, module, row: makeMyEnrolment({ moduleCode: 'CS3099' }) })).toBe(
      'enrolled',
    )
    expect(
      kind({
        ...base,
        module,
        row: makeMyEnrolment({ canWithdraw: false, withdrawBlockedReason: 'results' }),
      }),
    ).toBe('enrolledResults')
    expect(
      kind({
        ...base,
        module,
        row: makeMyEnrolment({ canWithdraw: false, withdrawBlockedReason: 'deadline' }),
      }),
    ).toBe('enrolledDeadline')
    expect(
      kind({
        ...base,
        module,
        row: makeMyEnrolment({
          academicYear: '2025/26',
          canWithdraw: false,
          withdrawBlockedReason: 'year',
        }),
      }),
    ).toBe('completed')
    expect(kind({ ...base, module: makeModule({ isActive: false }), row: undefined })).toBe(
      'inactive',
    )
    expect(kind({ ...base, module: makeModule({ enrolledCount: 30 }), row: undefined })).toBe(
      'full',
    )
    expect(
      kind({ ...base, module: makeModule({ enrolmentState: 'notYetOpen' }), row: undefined }),
    ).toBe('notYetOpen')
    expect(
      kind({ ...base, module: makeModule({ enrolmentState: 'closed' }), row: undefined }),
    ).toBe('closed')
    expect(
      kind({ ...base, module: makeModule({ enrolmentState: 'noWindow' }), row: undefined }),
    ).toBe('noWindow')
    expect(kind({ ...base, module, row: undefined, creditsUsed: 50 })).toBe('overCredits')
    expect(
      kind({
        ...base,
        module,
        row: makeMyEnrolment({ moduleCode: 'CS3099', status: 'withdrawn', canWithdraw: false }),
      }),
    ).toBe('enrolAgain')
    expect(kind({ ...base, module, row: undefined })).toBe('enrol')
  })

  it('disables "Over credit limit" only while the enrolments are fresh', () => {
    const module = makeModule()
    expect(deriveEnrolState({ ...base, module, row: undefined, creditsUsed: 50 }).disabled).toBe(
      true,
    )
    expect(
      deriveEnrolState({
        ...base,
        module,
        row: undefined,
        creditsUsed: 50,
        enrolmentsFresh: false,
      }).disabled,
    ).toBe(false)
  })

  it('counts this year’s active credits per semester', () => {
    const rows = [
      makeMyEnrolment({ moduleCode: 'A', credits: 15 }),
      makeMyEnrolment({ moduleCode: 'B', credits: 30, semester: 'spring' }),
      makeMyEnrolment({ moduleCode: 'C', credits: 15, status: 'withdrawn' }),
      makeMyEnrolment({ moduleCode: 'D', credits: 15, academicYear: '2025/26' }),
    ]
    expect(creditsUsed(rows, 'autumn', '2026/27')).toBe(15)
    expect(creditsUsed(rows, 'spring', '2026/27')).toBe(30)
    expect(creditsUsed(undefined, 'spring', '2026/27')).toBe(0)
    expect([...enrolledThisYear(rows, '2026/27')]).toEqual(['A', 'B'])
    expect(isCurrentYearRow(makeMyEnrolment({ withdrawBlockedReason: 'year' }), undefined)).toBe(
      false,
    )
  })
})

describe('catalogue filters', () => {
  const modules = [
    makeModule({ code: 'CS1001', title: 'Programming' }),
    makeModule({ code: 'MA2004', title: 'Linear Algebra', semester: 'spring' }),
    makeModule({ code: 'PH3001', title: 'Quantum', enrolledCount: 30 }),
  ]

  it('reads known values from the URL and ignores the rest', () => {
    const filters = readFilters(
      new URLSearchParams('q= alg &semester=spring&level=9&dept=MA&availability=full&mine=1'),
    )
    expect(filters).toEqual({
      q: 'alg',
      semester: 'spring',
      level: null,
      dept: 'MA',
      availability: 'full',
      mine: true,
    })
    expect(hasFilters(readFilters(new URLSearchParams()))).toBe(false)
  })

  it('filters by text, semester, level, department, availability and enrolment', () => {
    const codes = (query: string, enrolled: string[] = []) =>
      filterModules(modules, readFilters(new URLSearchParams(query)), new Set(enrolled)).map(
        (module) => module.code,
      )
    expect(codes('q=algebra')).toEqual(['MA2004'])
    expect(codes('q=cs1')).toEqual(['CS1001'])
    expect(codes('semester=spring')).toEqual(['MA2004'])
    expect(codes('level=3')).toEqual(['PH3001'])
    expect(codes('dept=CS')).toEqual(['CS1001'])
    expect(codes('availability=full')).toEqual(['PH3001'])
    expect(codes('availability=available')).toEqual(['CS1001', 'MA2004'])
    expect(codes('mine=1', ['MA2004'])).toEqual(['MA2004'])
  })
})

describe('copy', () => {
  it('words windows, publications and budgets with the zone named', () => {
    expect(
      windowClosedSentence(
        {
          semester: 'autumn',
          enrolmentState: 'closed',
          windowOpensAt: null,
          windowClosesAt: '2026-10-02T16:00:00.000Z',
        },
        'Europe/London',
      ),
    ).toBe('Enrolment for Autumn closed on 2 October 2026 at 17:00 (BST).')
    expect(
      windowClosedSentence(
        {
          semester: 'spring',
          enrolmentState: 'notYetOpen',
          windowOpensAt: '2027-01-04T09:00:00.000Z',
          windowClosesAt: null,
        },
        'Europe/London',
      ),
    ).toBe('Enrolment for Spring opens 4 January 2027 at 09:00 (GMT).')
    expect(
      windowClosedSentence(
        {
          semester: 'spring',
          enrolmentState: 'noWindow',
          windowOpensAt: null,
          windowClosesAt: null,
        },
        'Europe/London',
      ),
    ).toBe('Enrolment dates for Spring have not been announced yet.')
    expect(
      publicationSentence(
        {
          academicYear: '2025/26',
          semester: 'autumn',
          publishAt: '2026-09-28T09:00:00.000Z',
          state: 'scheduled',
        },
        'Europe/London',
      ),
    ).toBe('Autumn 2025/26 results publish 28 September 2026 at 10:00 (BST)')
    expect(creditBudgetText(15, 60, 'autumn', '2026/27')).toBe('15 of 60 credits, Autumn 2026/27')
  })
})

describe('time and timetable', () => {
  it('reads the wall clock in the institution zone', () => {
    const moment = zonedMoment(Date.parse('2026-09-28T23:30:00.000Z'), 'Europe/London')
    expect(moment).toMatchObject({ weekday: 'tuesday', day: 29, hour: 0, minutes: 30 })
    expect(minutesOf('09:30')).toBe(570)
    expect(minutesOf('nonsense')).toBe(0)
    expect([greeting(8), greeting(13), greeting(20)]).toEqual([
      'Good morning',
      'Good afternoon',
      'Good evening',
    ])
  })

  it('finds today’s remaining classes, else the next day with classes', () => {
    const timetable = [makeTimetableSlot({ day: 'monday', startTime: '09:00', endTime: '11:00' })]
    expect(nextClasses(timetable, { weekday: 'monday', minutes: 8 * 60 })?.label).toBe('Today')
    expect(nextClasses(timetable, { weekday: 'sunday', minutes: 12 * 60 })?.label).toBe('Tomorrow')
    // After Monday's class, the next is Monday next week.
    expect(nextClasses(timetable, { weekday: 'monday', minutes: 12 * 60 })?.label).toBe('Monday')
    expect(nextClasses([], { weekday: 'monday', minutes: 0 })).toBeNull()
  })

  it('splits overlapping classes into lanes', () => {
    const slot = (code: string, startTime: string, endTime: string) => ({
      moduleCode: code,
      moduleTitle: code,
      semester: 'autumn' as const,
      day: 'monday' as const,
      startTime,
      endTime,
      room: 'R',
      kind: 'lecture' as const,
    })
    const placed = placeDay([
      slot('A', '09:00', '11:00'),
      slot('B', '10:00', '12:00'),
      slot('C', '13:00', '14:00'),
    ])
    expect(placed.map(({ entry, lane, lanes }) => [entry.moduleCode, lane, lanes])).toEqual([
      ['A', 0, 2],
      ['B', 1, 2],
      ['C', 0, 1],
    ])
  })
})
