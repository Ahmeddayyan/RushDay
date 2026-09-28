import { ApiError } from './client'

export interface ProblemDescription {
  title: string
  message: string
  action?: 'retry' | 'login' | 'wait'
}

/**
 * Maps a thrown error to user-facing copy. This is the S3 skeleton used by `ErrorState`; the full
 * slug-keyed catalogue of every ProblemDetails type in 05-frontend.md section 6.4 is built in stage S5.
 */
export function describeProblem(error: unknown): ProblemDescription {
  if (error instanceof ApiError) {
    if (error.status === 0) {
      return {
        title: 'Network error',
        message: 'The server could not be reached. Check your connection and try again.',
        action: 'retry',
      }
    }
    return {
      title: error.problem?.title ?? 'Something went wrong',
      message: error.problem?.detail ?? error.message,
    }
  }
  if (error instanceof Error && error.message) {
    return { title: 'Something went wrong', message: error.message }
  }
  return { title: 'Something went wrong', message: 'Try again in a moment.' }
}
