import { ApiError } from '@/lib/api'

/** The sentence shown under the sign-in form when the login request fails. */
export function loginErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const message = error.detail ?? error.title
    // The auth endpoints are not deployed yet; say so instead of a bare "Not found".
    if (error.status === 404) return `${message}. Sign-in is not available on this server yet.`
    return message
  }
  if (error instanceof Error && error.message) return error.message
  return 'Sign-in failed. Try again in a moment.'
}
