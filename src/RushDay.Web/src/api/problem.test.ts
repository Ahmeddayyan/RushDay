import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiError, NetworkError, type ProblemDetails } from './client'
import {
  describeProblem,
  fieldErrors,
  isProblem,
  mapFieldErrors,
  PROBLEM_CATALOGUE,
  PROBLEM_COPY,
  weakPasswordReasons,
  type ProblemSlug,
} from './problem'

function apiError(
  slug: string,
  status: number,
  extensions: Partial<ProblemDetails> = {},
  headers?: Headers,
) {
  return new ApiError(
    status,
    { type: `urn:rushday:${slug}`, title: slug, status, traceId: '00-trace-01', ...extensions },
    headers,
  )
}

const GENERIC_MESSAGES = ['Try again in a moment.', '']

afterEach(() => {
  vi.useRealTimers()
})

describe('the ProblemDetails catalogue', () => {
  it('holds exactly the 63 slugs of 02-api.md section 6', () => {
    expect(Object.keys(PROBLEM_CATALOGUE)).toHaveLength(63)
  })

  it('has a copy row for every slug', () => {
    expect(Object.keys(PROBLEM_COPY).sort()).toEqual(Object.keys(PROBLEM_CATALOGUE).sort())
  })

  it.each(Object.entries(PROBLEM_CATALOGUE) as [ProblemSlug, number][])(
    'maps %s (%i) to specific copy',
    (slug, status) => {
      const described = describeProblem(apiError(slug, status))
      expect(described.title.length).toBeGreaterThan(0)
      expect(GENERIC_MESSAGES).not.toContain(described.message)
      // Specific copy never falls back to the raw slug title the server sends.
      expect(described.message).not.toBe(slug)
    },
  )
})

describe('describeProblem copy', () => {
  it('uses the exact sentences of 05-frontend.md section 6.4', () => {
    expect(describeProblem(apiError('invalid-credentials', 401)).message).toBe(
      'Incorrect username or password.',
    )
    expect(describeProblem(apiError('invalid-mfa-code', 400)).message).toBe(
      "That code didn't work. Check the time on your phone and try the newest code.",
    )
    expect(describeProblem(apiError('antiforgery', 400)).message).toBe(
      'Your page is out of date. Reload and try again.',
    )
    expect(describeProblem(apiError('demo-account', 409)).message).toBe(
      'Demo accounts are read-only, so the demo stays usable for the next visitor.',
    )
    expect(describeProblem(apiError('module-full', 409), { code: 'CS3099' }).message).toBe(
      'CS3099 is full.',
    )
    expect(
      describeProblem(apiError('not-module-leader', 403), { leader: 'Dr Grace Hopper' }).message,
    ).toBe('Only the module leader, Dr Grace Hopper, can submit these marks.')
    expect(
      describeProblem(apiError('stale-mark', 409, { studentNumbers: ['S000001', 'S000002'] }))
        .message,
    ).toBe('Someone else changed 2 marks. Review the highlighted rows and save again.')
    expect(
      describeProblem(
        apiError('not-enrolled-students', 422, { studentNumbers: ['S000001', 'S000009'] }),
      ).message,
    ).toBe('2 students are no longer enrolled: S000001, S000009.')
    expect(
      describeProblem(
        apiError('credit-limit-exceeded', 422, {
          limit: 60,
          semester: 'spring',
          currentCredits: 60,
        }),
      ).message,
    ).toBe('That would take you over 60 credits for Spring.')
    expect(
      describeProblem(apiError('marks-incomplete', 422, { missing: ['S1', 'S2', 'S3'] })).message,
    ).toBe('3 students have no mark or outcome yet.')
    expect(
      describeProblem(apiError('capacity-below-enrolled', 422, { enrolledCount: 31 })).message,
    ).toBe("Capacity can't go below the 31 students already enrolled.")
    expect(
      describeProblem(apiError('nothing-to-publish', 409), {
        semester: 'autumn',
        academicYear: '2026/27',
      }).message,
    ).toBe(
      'No submitted modules are ready to publish for Autumn 2026/27. Lecturers submit modules from their Marks page.',
    )
    expect(
      describeProblem(apiError('window-exists', 409), {
        semester: 'spring',
        academicYear: '2026/27',
      }).message,
    ).toBe('A window for 2026/27 Spring already exists. Edit it instead.')
    expect(
      describeProblem(apiError('principal-has-account', 409), { number: 'S000123' }).message,
    ).toBe('S000123 already has an account.')
    expect(describeProblem(apiError('module-code-taken', 409), { value: 'CS9999' }).message).toBe(
      'CS9999 already exists.',
    )
  })

  it('words module-locked for the audience', () => {
    expect(describeProblem(apiError('module-locked', 409)).message).toMatch(
      /Ask the academic office to return it to draft/,
    )
    expect(describeProblem(apiError('module-locked', 409), { audience: 'admin' }).message).toBe(
      'Students can already see these marks. Unpublish the semester or correct single marks.',
    )
  })

  it('words module-locked on an enrolment for the student and for the registry (S6 review E1)', () => {
    expect(
      describeProblem(apiError('module-locked', 409), {
        code: 'CS3001',
        enrolment: true,
        support: null,
      }).message,
    ).toBe(
      "Marks for CS3001 have already been submitted this year, so you can't join it now. Contact the academic office.",
    )
    expect(
      describeProblem(apiError('module-locked', 409), {
        code: 'CS3001',
        audience: 'admin',
        enrolment: true,
      }).message,
    ).toBe(
      'Marks for CS3001 are already submitted this year. Return the module to draft before enrolling anyone.',
    )
  })

  it('words principal-left and the any-year semester guard (S6 review)', () => {
    expect(describeProblem(apiError('principal-left', 409), { number: 'S000123' }).message).toBe(
      "S000123 has left, so they can't have an account.",
    )
    expect(
      describeProblem(apiError('semester-change-with-enrolments', 422, { enrolledCount: 412 }))
        .message,
    ).toBe(
      "The semester can't change once students have enrolled (412 enrolments in all years). Create a new module instead.",
    )
  })

  it('words student-left for the student themselves', () => {
    expect(describeProblem(apiError('student-left', 409)).message).toBe(
      "This student has left, so they can't be enrolled.",
    )
    expect(describeProblem(apiError('student-left', 409), { ownSession: true }).message).toBe(
      'Your record is marked as left. Contact the academic office.',
    )
  })

  it('names the academic office with its contact details when the institution set them', () => {
    const withEmail = describeProblem(apiError('results-exist', 409), {
      code: 'CS3001',
      support: { email: 'registry@example.ac.uk', url: null },
    })
    expect(withEmail.message).toBe(
      "CS3001 already has a submitted or published mark, so it can't be changed here. Contact the academic office at registry@example.ac.uk.",
    )
    const withUrl = describeProblem(
      apiError('withdrawal-deadline-passed', 409, { withdrawalDeadlineAt: '2026-10-30T17:00:00Z' }),
      {
        code: 'CS3001',
        support: { email: null, url: 'https://help.example.ac.uk' },
      },
    )
    expect(withUrl.message).toBe(
      'The withdrawal deadline for CS3001 was 30 October 2026 at 17:00 (GMT). To withdraw now, contact the academic office (https://help.example.ac.uk).',
    )
  })

  describe('enrolment-window-closed', () => {
    const now = Date.parse('2026-09-28T12:00:00Z')

    it('says when a window that has not opened yet opens', () => {
      vi.useFakeTimers()
      vi.setSystemTime(now)
      const error = apiError('enrolment-window-closed', 409, {
        semester: 'spring',
        opensAt: '2027-01-11T09:00:00Z',
        closesAt: '2027-01-29T17:00:00Z',
      })
      expect(describeProblem(error).message).toBe(
        'Enrolment for Spring opens 11 January 2027 at 09:00 (GMT).',
      )
    })

    it('says when a closed window closed', () => {
      vi.useFakeTimers()
      vi.setSystemTime(now)
      const error = apiError('enrolment-window-closed', 409, {
        semester: 'autumn',
        opensAt: '2026-09-14T09:00:00Z',
        closesAt: '2026-09-25T17:00:00Z',
      })
      expect(describeProblem(error).message).toBe(
        'Enrolment for Autumn closed on 25 September 2026 at 18:00 (BST).',
      )
    })

    it('says the dates are not announced when there is no window', () => {
      const error = apiError('enrolment-window-closed', 409, {
        semester: 'autumn',
        opensAt: null,
        closesAt: null,
      })
      expect(describeProblem(error).message).toBe(
        'Enrolment dates for Autumn have not been announced yet.',
      )
    })
  })

  it('reads Retry-After for rate limits and busy answers', () => {
    const limited = describeProblem(
      apiError('rate-limited', 429, {}, new Headers({ 'Retry-After': '42' })),
    )
    expect(limited).toMatchObject({
      message: 'Too many attempts. Try again in 42s.',
      action: 'wait',
      retryAfterSeconds: 42,
    })

    const busy = describeProblem(
      apiError('server-busy', 503, {}, new Headers({ 'Retry-After': '3' })),
    )
    expect(busy).toMatchObject({
      message: 'The portal is very busy right now. Try again in a moment.',
      action: 'wait',
      retryAfterSeconds: 3,
    })
    expect(describeProblem(apiError('timeout', 503)).retryAfterSeconds).toBe(2)
  })

  it('lists weak-password reasons', () => {
    const error = apiError('weak-password', 400, { errors: { newPassword: ['same-as-current'] } })
    expect(describeProblem(error).message).toBe(
      'Choose a password different from your current one.',
    )
    expect(
      weakPasswordReasons(
        apiError('weak-password', 400, { errors: { newPassword: ['PasswordTooShort', 'Odd'] } }),
      ),
    ).toEqual(['Use at least 12 characters.', 'Choose a stronger password.'])
    expect(weakPasswordReasons(new Error('x'))).toEqual(['Choose a stronger password.'])
  })

  it('joins validation messages', () => {
    const error = apiError('validation', 400, {
      errors: { username: ['The Username field is required.'] },
    })
    expect(describeProblem(error).message).toBe('The Username field is required.')
    expect(describeProblem(apiError('validation', 400)).message).toBe(
      'Check the highlighted fields and try again.',
    )
  })
})

describe('fallbacks', () => {
  it('uses the problem detail for an unknown 4xx', () => {
    const error = new ApiError(
      418,
      { type: 'urn:rushday:teapot', title: 'Teapot', detail: 'Short and stout.' },
      undefined,
    )
    expect(describeProblem(error)).toMatchObject({ message: 'Short and stout.' })
    const noDetail = new ApiError(418, { title: 'Teapot' }, undefined)
    expect(describeProblem(noDetail).message).toBe('Teapot')
  })

  it('includes the traceId for an unknown 5xx', () => {
    const error = new ApiError(502, { title: 'Bad gateway', traceId: '00-abc-01' }, undefined)
    expect(describeProblem(error).message).toBe(
      'Something went wrong on our side. Reference 00-abc-01.',
    )
    expect(describeProblem(new ApiError(504, undefined, undefined)).message).toBe(
      'Something went wrong on our side. Reference unavailable.',
    )
  })

  it('falls back by status when the slug is missing', () => {
    expect(describeProblem(new ApiError(404, undefined, undefined)).message).toBe(
      "That page or record doesn't exist.",
    )
    expect(describeProblem(new ApiError(429, undefined, undefined)).action).toBe('wait')
    expect(describeProblem(new ApiError(500, { traceId: 't-1' }, undefined)).message).toBe(
      'Something went wrong on our side. Reference t-1.',
    )
  })

  it('describes a network failure', () => {
    expect(describeProblem(new NetworkError(new TypeError('Failed to fetch')))).toMatchObject({
      title: "Can't reach the server",
      action: 'retry',
    })
  })

  it('describes plain errors and anything else', () => {
    expect(describeProblem(new Error('kaboom')).message).toBe('kaboom')
    expect(describeProblem('nope')).toEqual({
      title: 'Something went wrong',
      message: 'Try again in a moment.',
    })
  })
})

describe('form helpers', () => {
  it('maps validation errors to known fields and keeps the rest', () => {
    const error = apiError('validation', 400, {
      errors: { CurrentPassword: ['Too long.'], somethingElse: ['Nope.'] },
    })
    expect(mapFieldErrors(error, ['currentPassword', 'newPassword'] as const)).toEqual({
      fields: { currentPassword: 'Too long.' },
      other: ['Nope.'],
    })
    expect(fieldErrors(apiError('module-full', 409))).toEqual({})
  })

  it('recognises slugs', () => {
    const error = apiError('module-full', 409)
    expect(isProblem(error, 'module-full', 'module-inactive')).toBe(true)
    expect(isProblem(error, 'already-enrolled')).toBe(false)
    expect(isProblem(new Error('x'), 'module-full')).toBe(false)
  })
})
