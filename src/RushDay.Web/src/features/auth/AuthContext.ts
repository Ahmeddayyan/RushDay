import { createContext, useContext } from 'react'

import type { AuthStatus, LoginCredentials, Role, SessionUser } from './types'

export interface AuthContextValue {
  status: AuthStatus
  user: SessionUser | null
  /** Why the session bootstrap failed, if it did for a reason other than "not signed in". */
  error: unknown
  signIn: (credentials: LoginCredentials) => Promise<SessionUser>
  signOut: () => Promise<void>
  /** True when the signed-in user holds at least one of the given roles. */
  hasRole: (...roles: Role[]) => boolean
}

export const sessionQueryKey = ['auth', 'session'] as const

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside <AuthProvider>.')
  return context
}
