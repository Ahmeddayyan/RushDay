import { describe, expect, it } from 'vitest'

import { queryKeys, queryTimings } from './keys'

describe('queryKeys', () => {
  it('spells the keys of 05-frontend.md section 6.2', () => {
    expect(queryKeys.index).toEqual(['index'])
    expect(queryKeys.publicStatus).toEqual(['public', 'status'])
    expect(queryKeys.authMe).toEqual(['auth', 'me'])
    expect(queryKeys.student.dashboard).toEqual(['student', 'dashboard'])
    expect(queryKeys.student.results).toEqual(['student', 'results'])
    expect(queryKeys.student.timetable).toEqual(['student', 'timetable'])
    expect(queryKeys.student.enrolments).toEqual(['student', 'enrolments'])
    expect(queryKeys.modules.catalogue).toEqual(['modules', 'catalogue'])
    expect(queryKeys.modules.detail('CS3099')).toEqual(['modules', 'CS3099'])
    expect(queryKeys.announcements).toEqual(['announcements'])
    expect(queryKeys.lecturer.modules).toEqual(['lecturer', 'modules'])
    expect(queryKeys.lecturer.roster('CS3001', { q: 'khan', page: 2, pageSize: 50 })).toEqual([
      'lecturer',
      'module',
      'CS3001',
      'roster',
      { q: 'khan', page: 2, pageSize: 50 },
    ])
    expect(queryKeys.lecturer.marks('CS3001', { q: '', page: 1 })).toEqual([
      'lecturer',
      'module',
      'CS3001',
      'marks',
      { q: '', page: 1 },
    ])
    expect(queryKeys.lecturer.moduleAnnouncements('CS3001')).toEqual([
      'lecturer',
      'module',
      'CS3001',
      'announcements',
    ])
    expect(queryKeys.admin.overview).toEqual(['admin', 'overview'])
    expect(queryKeys.admin.results('2026/27', 'autumn')).toEqual([
      'admin',
      'results',
      '2026/27',
      'autumn',
    ])
    expect(queryKeys.admin.lecturers('hop')).toEqual(['admin', 'lecturers', 'hop'])
    expect(queryKeys.admin.students({ q: 'S0001', page: 1 })).toEqual([
      'admin',
      'students',
      { q: 'S0001', page: 1 },
    ])
    expect(queryKeys.admin.accounts({ role: 'Admin' })).toEqual([
      'admin',
      'accounts',
      { role: 'Admin' },
    ])
    expect(queryKeys.admin.audit({ action: 'grade.corrected' })).toEqual([
      'admin',
      'audit',
      { action: 'grade.corrected' },
    ])
    expect(queryKeys.admin.student('S000001')).toEqual(['admin', 'student', 'S000001'])
    expect(queryKeys.admin.modules(false)).toEqual(['admin', 'modules', false])
    expect(queryKeys.admin.moduleRoster('CS3001', { page: 1 })).toEqual([
      'admin',
      'module',
      'CS3001',
      'roster',
      { page: 1 },
    ])
    expect(queryKeys.admin.moduleMarks('CS3001', { page: 1 })).toEqual([
      'admin',
      'module',
      'CS3001',
      'marks',
      { page: 1 },
    ])
    expect(queryKeys.admin.opsMetrics).toEqual(['admin', 'ops', 'metrics'])
    expect(queryKeys.loadResults).toEqual(['load-results'])
  })

  it('keeps per-module keys under a common prefix for invalidation', () => {
    const prefix = queryKeys.lecturer.module('CS3001')
    expect(queryKeys.lecturer.marks('CS3001', { q: '', page: 3 }).slice(0, prefix.length)).toEqual(
      prefix,
    )
    expect(queryKeys.admin.moduleMarks('CS3001', {}).slice(0, 3)).toEqual(
      queryKeys.admin.module('CS3001'),
    )
  })

  it('carries the freshness rules', () => {
    expect(queryTimings.index.staleTime).toBe(Infinity)
    expect(queryTimings.publicStatus.staleTime).toBe(60_000)
    expect(queryTimings.moduleDetail).toEqual({ staleTime: 5_000, refetchInterval: 10_000 })
    expect(queryTimings.opsMetrics).toEqual({
      refetchInterval: 5_000,
      refetchIntervalInBackground: false,
    })
  })
})
