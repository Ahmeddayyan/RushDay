import { describe, expect, it } from 'vitest'

import { makeResultsModule } from '@/test/handlers/admin'

import { AUDIT_ACTIONS, auditActionLabel, isAuditAction } from './auditActions'
import { dayOf, dayStart, presetRange } from './auditRange'
import { isAdministratorLock } from './accounts'
import { assignmentProblems, describeMark, type AssignmentRow } from './marks'
import {
  exclusionReason,
  isPublishable,
  publishPreview,
  recentAcademicYears,
  sortProgress,
} from './results'
import {
  correctMarkSchema,
  createStudentSchema,
  isAcademicYear,
  isTimeZone,
  settingsSchema,
} from './schemas'
import { closeNow, openNowFor7Days, validateWindow } from './windows'
import {
  nextNineAm,
  startOfZonedDay,
  toApiInstant,
  toZonedInput,
  zonedInputToInstant,
  zonedInputToMs,
  zoneOffsetMs,
} from '@/lib/zonedTime'

const LONDON = 'Europe/London'

describe('zonedTime', () => {
  it('reads wall-clock input in the institution zone, across both sides of the clock change', () => {
    // BST (UTC+1) in September, GMT (UTC+0) in December.
    expect(zonedInputToInstant('2026-09-28T09:00', LONDON)).toBe('2026-09-28T08:00:00.000Z')
    expect(zonedInputToInstant('2026-12-01T09:00', LONDON)).toBe('2026-12-01T09:00:00.000Z')
    // The morning after the October change is GMT.
    expect(zonedInputToInstant('2026-10-25T09:00', LONDON)).toBe('2026-10-25T09:00:00.000Z')
    expect(zonedInputToInstant('2026-09-28T09:00', 'America/New_York')).toBe(
      '2026-09-28T13:00:00.000Z',
    )
  })

  it('writes instants back as datetime-local text in the zone', () => {
    expect(toZonedInput('2026-09-28T08:00:00.000Z', LONDON)).toBe('2026-09-28T09:00')
    expect(toZonedInput(Date.parse('2027-01-29T17:00:00Z'), LONDON)).toBe('2027-01-29T17:00')
    expect(toZonedInput('not a date', LONDON)).toBe('')
  })

  it('rejects incomplete input and uses three fractional digits', () => {
    expect(zonedInputToMs('2026-09-28', LONDON)).toBeNull()
    expect(zonedInputToMs('', LONDON)).toBeNull()
    expect(zonedInputToInstant('28/09/2026 09:00', LONDON)).toBeNull()
    expect(toApiInstant(Date.parse('2026-09-28T08:00:00Z'))).toMatch(/\.\d{3}Z$/)
  })

  it('knows the offset and the start of the zone day', () => {
    expect(zoneOffsetMs(Date.parse('2026-07-01T12:00:00Z'), LONDON)).toBe(3_600_000)
    expect(zoneOffsetMs(Date.parse('2026-01-01T12:00:00Z'), LONDON)).toBe(0)
    const noon = Date.parse('2026-09-29T11:00:00Z')
    expect(toApiInstant(startOfZonedDay(noon, LONDON))).toBe('2026-09-28T23:00:00.000Z')
    expect(toApiInstant(startOfZonedDay(noon, LONDON, 1))).toBe('2026-09-29T23:00:00.000Z')
  })

  it('offers the next 09:00 in the zone as the default results-day instant', () => {
    expect(toApiInstant(nextNineAm(Date.parse('2026-09-29T06:00:00Z'), LONDON))).toBe(
      '2026-09-29T08:00:00.000Z',
    )
    expect(toApiInstant(nextNineAm(Date.parse('2026-09-29T12:00:00Z'), LONDON))).toBe(
      '2026-09-30T08:00:00.000Z',
    )
  })

  it('falls back to the default zone for an unknown zone id', () => {
    expect(toZonedInput('2026-09-28T08:00:00.000Z', 'Not/AZone')).toBe('2026-09-28T09:00')
  })
})

describe('results preview', () => {
  const modules = [
    makeResultsModule('CS3001', { status: 'submitted', entered: 100, missing: 0, total: 100 }),
    makeResultsModule('CS3099', { status: 'submitted', entered: 28, missing: 2, total: 30 }),
    makeResultsModule('MA1001', { status: 'draft', entered: 0, missing: 40, total: 40 }),
    makeResultsModule('PH2001', { status: 'noStudents', missing: 0, total: 0 }),
    makeResultsModule('EE1001', { status: 'published', entered: 10, missing: 0, total: 10 }),
  ]

  it('publishes only submitted modules with nothing missing, and says why the rest are left out', () => {
    const preview = publishPreview(modules)
    expect(preview.modules.map((module) => module.code)).toEqual(['CS3001'])
    expect(preview.grades).toBe(100)
    expect(preview.excluded.map((item) => [item.code, exclusionReason(item)])).toEqual([
      ['CS3099', '2 marks missing'],
      ['MA1001', 'not submitted'],
    ])
    expect(isPublishable(modules[0]!)).toBe(true)
    expect(
      exclusionReason({ code: 'X', status: 'submitted', reason: 'marksMissing', marksMissing: 1 }),
    ).toBe('1 mark missing')
  })

  it('sorts by state then code, with modules without students last', () => {
    expect(sortProgress(modules).map((module) => module.code)).toEqual([
      'CS3001',
      'CS3099',
      'MA1001',
      'EE1001',
      'PH2001',
    ])
  })

  it('lists the recent academic years', () => {
    expect(recentAcademicYears('2026/27')).toEqual(['2026/27', '2025/26', '2024/25', '2023/24'])
    expect(recentAcademicYears('2099/00', 2)).toEqual(['2099/00', '2098/99'])
    expect(recentAcademicYears('')).toEqual([])
  })
})

describe('marks and assignments', () => {
  const row = (staffNumber: string | null, role: 'leader' | 'teacher', left = false) =>
    ({
      key: Math.random(),
      lecturer: staffNumber ? { staffNumber, name: staffNumber, left } : null,
      role,
    }) satisfies AssignmentRow

  it('describes a mark in one word', () => {
    expect(describeMark({ outcome: 'mark', mark: 68 })).toBe('68')
    expect(describeMark({ outcome: 'absent', mark: null })).toBe('Absent')
    expect(describeMark({ outcome: 'deferred', mark: null })).toBe('Deferred')
    expect(describeMark({ outcome: null, mark: null })).toBe('no mark')
  })

  it('enforces exactly one leader, each lecturer once and nobody who has left', () => {
    expect(assignmentProblems([row('L00001', 'leader'), row('L00006', 'teacher')])).toEqual([])
    expect(assignmentProblems([])).toContain('Assign at least the module leader.')
    expect(assignmentProblems([row('L00001', 'teacher')])).toContain('Choose one leader.')
    expect(assignmentProblems([row('L00001', 'leader'), row('L00002', 'leader')])).toContain(
      'Only one leader is allowed; 2 are chosen.',
    )
    expect(assignmentProblems([row('L00001', 'leader'), row('L00001', 'teacher')])).toContain(
      'List each lecturer once.',
    )
    expect(assignmentProblems([row('L00001', 'leader', true)])).toContain(
      "Remove lecturers who have left; they can't be assigned.",
    )
    expect(assignmentProblems([row(null, 'leader')])).toContain('Choose a lecturer on every row.')
  })
})

describe('windows', () => {
  it('mirrors window-dates-invalid before sending', () => {
    expect(
      validateWindow(
        { opensAt: '2026-10-02T09:00', closesAt: '2026-09-14T09:00', withdrawalDeadlineAt: '' },
        LONDON,
      ).errors,
    ).toEqual({
      closesAt: 'Closes must be after opens.',
      withdrawalDeadlineAt: 'Enter the withdrawal deadline.',
    })
    expect(
      validateWindow(
        {
          opensAt: '2026-09-14T09:00',
          closesAt: '2026-10-02T17:00',
          withdrawalDeadlineAt: '2026-10-01T17:00',
        },
        LONDON,
      ).errors.withdrawalDeadlineAt,
    ).toBe("The withdrawal deadline can't be before closes.")
    const valid = validateWindow(
      {
        opensAt: '2026-09-14T10:00',
        closesAt: '2026-10-02T18:00',
        withdrawalDeadlineAt: '2026-10-30T17:00',
      },
      LONDON,
    )
    expect(valid.instants).toEqual({
      opensAt: '2026-09-14T09:00:00.000Z',
      closesAt: '2026-10-02T17:00:00.000Z',
      withdrawalDeadlineAt: '2026-10-30T17:00:00.000Z',
    })
  })

  it('computes the open-now and close-now shortcuts', () => {
    const now = Date.parse('2026-09-29T12:00:00Z')
    expect(openNowFor7Days({ withdrawalDeadlineAt: '2026-10-30T17:00:00Z' }, now)).toEqual({
      opensAt: '2026-09-29T12:00:00.000Z',
      closesAt: '2026-10-06T12:00:00.000Z',
      withdrawalDeadlineAt: '2026-10-30T17:00:00.000Z',
    })
    expect(openNowFor7Days({ withdrawalDeadlineAt: '2026-10-01T00:00:00Z' }, now)).toMatchObject({
      withdrawalDeadlineAt: '2026-10-06T12:00:00.000Z',
    })
    expect(
      closeNow(
        { opensAt: '2026-10-01T09:00:00Z', withdrawalDeadlineAt: '2026-09-01T00:00:00Z' },
        now,
      ),
    ).toEqual({
      opensAt: '2026-09-29T11:59:00.000Z',
      closesAt: '2026-09-29T12:00:00.000Z',
      withdrawalDeadlineAt: '2026-09-29T12:00:00.000Z',
    })
  })
})

describe('schemas', () => {
  it('checks academic years and time zones', () => {
    expect(isAcademicYear('2026/27')).toBe(true)
    expect(isAcademicYear('2099/00')).toBe(true)
    expect(isAcademicYear('2026/28')).toBe(false)
    expect(isAcademicYear('2026-27')).toBe(false)
    expect(isTimeZone('Europe/London')).toBe(true)
    expect(isTimeZone('America/Argentina/Buenos_Aires')).toBe(true)
    expect(isTimeZone('London')).toBe(false)
    expect(isTimeZone('Mars/Olympus_Mons')).toBe(false)
  })

  it('upper-cases student numbers and validates the correction mark only for Mark', () => {
    const parsed = createStudentSchema.parse({
      studentNumber: ' s000123 ',
      fullName: 'Ada',
      programme: 'BSc',
      yearOfStudy: '1',
      email: '',
      provision: true,
    })
    expect(parsed.studentNumber).toBe('S000123')
    expect(
      correctMarkSchema.safeParse({ outcome: 'mark', mark: '101', reason: 'Exam board decision' })
        .success,
    ).toBe(false)
    expect(
      correctMarkSchema.safeParse({ outcome: 'absent', mark: '', reason: 'Exam board decision' })
        .success,
    ).toBe(true)
  })

  it('validates settings fields', () => {
    const base = {
      academicYear: '2026/27',
      currentSemester: 'autumn',
      institutionName: 'Northbridge',
      institutionShortName: 'NB',
      timeZone: 'Europe/London',
      supportEmail: '',
      supportUrl: '',
    }
    expect(settingsSchema.safeParse(base).success).toBe(true)
    expect(settingsSchema.safeParse({ ...base, supportUrl: 'http://x.example' }).success).toBe(
      false,
    )
    expect(settingsSchema.safeParse({ ...base, supportEmail: 'not-an-email' }).success).toBe(false)
  })
})

describe('audit helpers', () => {
  it('labels every catalogue action and falls back to the key', () => {
    expect(AUDIT_ACTIONS).toHaveLength(48)
    expect(new Set(AUDIT_ACTIONS.map((entry) => entry.action)).size).toBe(AUDIT_ACTIONS.length)
    expect(auditActionLabel('grade.corrected')).toBe('Mark corrected')
    expect(auditActionLabel('future.action')).toBe('future.action')
    expect(isAuditAction('results.published')).toBe(true)
    expect(isAuditAction('nope')).toBe(false)
  })

  it('turns presets and custom days into instants in the zone', () => {
    const now = Date.parse('2026-09-29T12:00:00Z')
    expect(presetRange('today', now, LONDON)).toEqual({
      from: '2026-09-28T23:00:00.000Z',
      to: '2026-09-29T23:00:00.000Z',
    })
    expect(presetRange('7d', now, LONDON).from).toBe('2026-09-22T23:00:00.000Z')
    expect(presetRange('30d', now, LONDON).from).toBe('2026-08-30T23:00:00.000Z')
    expect(dayStart('2026-09-01', LONDON)).toBe('2026-08-31T23:00:00.000Z')
    expect(dayStart('2026-09-30', LONDON, 1)).toBe('2026-09-30T23:00:00.000Z')
    expect(dayStart('nope', LONDON)).toBeNull()
    expect(dayOf('2026-08-31T23:00:00.000Z', LONDON)).toBe('2026-09-01')
    expect(dayOf('2026-09-30T23:00:00.000Z', LONDON, -1)).toBe('2026-09-30')
    expect(dayOf('bad', LONDON)).toBe('')
  })

  it('recognises the administrator lock', () => {
    expect(isAdministratorLock('9999-12-31T00:00:00.000Z')).toBe(true)
    expect(isAdministratorLock('2026-09-29T12:15:00.000Z')).toBe(false)
    expect(isAdministratorLock(null)).toBe(false)
  })
})
