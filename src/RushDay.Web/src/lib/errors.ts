import { ApiError } from './api'

/** Turns any thrown value into a sentence a person can act on. */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) return error.detail ?? error.title
  if (error instanceof Error && error.message) return error.message
  return 'Something went wrong. Try again in a moment.'
}
