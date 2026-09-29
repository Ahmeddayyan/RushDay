import { describe, expect, it } from 'vitest'

import { loginPathFor, sanitizeReturnTo } from './returnTo'

const origin = 'https://portal.example.ac.uk'

describe('sanitizeReturnTo', () => {
  it.each([
    ['/student/results', '/student/results'],
    ['/admin/accounts?role=Admin&page=2', '/admin/accounts?role=Admin&page=2'],
    ['/student#timetable', '/student#timetable'],
    [`${origin}/lecturer`, '/lecturer'],
  ])('keeps the same-origin path %s', (input, expected) => {
    expect(sanitizeReturnTo(input, origin)).toBe(expected)
  })

  it.each([
    ['//evil'],
    ['//evil.example/login'],
    ['/\\evil.com'],
    ['\\\\evil.com'],
    ['https://evil.example'],
    ['http://portal.example.ac.uk/student'],
    ['javascript:alert(1)'],
    ['/login'],
    ['/login?returnTo=%2Fadmin'],
    ['/student\n/results'],
    ['/student\t'],
    [''],
  ])('rejects %j', (input) => {
    expect(sanitizeReturnTo(input, origin)).toBeNull()
  })

  it('rejects null and undefined', () => {
    expect(sanitizeReturnTo(null, origin)).toBeNull()
    expect(sanitizeReturnTo(undefined, origin)).toBeNull()
  })

  it('uses the page origin by default', () => {
    expect(sanitizeReturnTo('/account')).toBe('/account')
    expect(sanitizeReturnTo('https://evil.example/account')).toBeNull()
  })
})

describe('loginPathFor', () => {
  it('encodes the path and query as returnTo', () => {
    expect(loginPathFor('/student', '')).toBe('/login?returnTo=%2Fstudent')
    expect(loginPathFor('/admin/audit', '?page=2')).toBe(
      '/login?returnTo=%2Fadmin%2Faudit%3Fpage%3D2',
    )
  })
})
