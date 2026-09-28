import { describe, expect, it } from 'vitest'

import { ApiError, NetworkError } from './client'
import { describeProblem } from './problem'

describe('describeProblem', () => {
  it('uses the ProblemDetails title and detail when present', () => {
    const error = new ApiError(
      409,
      { title: 'Module full', detail: 'CS3099 has no places remaining.' },
      undefined,
    )
    expect(describeProblem(error)).toEqual({ title: 'Module full', message: 'CS3099 has no places remaining.' })
  })

  it('falls back to the error message when there is no ProblemDetails', () => {
    const error = new ApiError(502, undefined, undefined)
    expect(describeProblem(error).message).toBe('HTTP 502')
  })

  it('gives a network-error message and a retry action for status 0', () => {
    const error = new NetworkError(new Error('offline'))
    expect(describeProblem(error)).toMatchObject({ title: 'Network error', action: 'retry' })
  })

  it('uses a plain Error message', () => {
    expect(describeProblem(new Error('kaboom')).message).toBe('kaboom')
  })

  it('gives a generic message for anything else', () => {
    expect(describeProblem('nope')).toEqual({
      title: 'Something went wrong',
      message: 'Try again in a moment.',
    })
  })
})
