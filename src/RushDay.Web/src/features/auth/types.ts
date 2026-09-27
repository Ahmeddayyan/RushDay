/**
 * Session contract the SPA expects from the API. The auth endpoints do not exist yet;
 * adjust these types together with the spec when they land.
 *
 *   GET  /api/auth/me      -> 200 SessionUser | 401 (no session)
 *   POST /api/auth/login   -> 200 SessionUser | 401 ProblemDetails (bad credentials)
 *   POST /api/auth/logout  -> 204
 */

export type Role = 'Student' | 'Staff' | 'Admin'

export interface SessionUser {
  id: string
  /** Student number (S000001) or staff username. */
  username: string
  displayName: string
  roles: Role[]
}

export interface LoginCredentials {
  /** Student number or username. */
  identifier: string
  password: string
}

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous'
