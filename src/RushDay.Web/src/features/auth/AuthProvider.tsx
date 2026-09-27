import { useCallback, useMemo, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'

import { ApiError, api } from '@/lib/api'

import { AuthContext, sessionQueryKey, type AuthContextValue } from './AuthContext'
import type { AuthStatus, LoginCredentials, Role, SessionUser } from './types'

/**
 * Bootstraps the session from GET /api/auth/me. 401 means signed out; 404 means the endpoint
 * is not deployed yet. Both are ordinary "anonymous" states, not failures.
 */
async function fetchSession(signal: AbortSignal): Promise<SessionUser | null> {
  try {
    return await api.get<SessionUser>('/api/auth/me', { signal })
  } catch (error) {
    if (error instanceof ApiError && (error.status === 401 || error.status === 404)) return null
    throw error
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()

  const session = useQuery({
    queryKey: sessionQueryKey,
    queryFn: ({ signal }) => fetchSession(signal),
    retry: false,
    staleTime: Infinity,
  })

  const user = session.data ?? null
  const status: AuthStatus = session.isPending ? 'loading' : user ? 'authenticated' : 'anonymous'

  const signIn = useCallback(
    async (credentials: LoginCredentials) => {
      const signedIn = await api.post<SessionUser>('/api/auth/login', credentials)
      queryClient.setQueryData(sessionQueryKey, signedIn)
      return signedIn
    },
    [queryClient],
  )

  const signOut = useCallback(async () => {
    try {
      await api.post<void>('/api/auth/logout')
    } catch (error) {
      // Already signed out, or the endpoint is not there yet: the local session goes either way.
      if (!(error instanceof ApiError && (error.status === 401 || error.status === 404)))
        throw error
    } finally {
      queryClient.setQueryData(sessionQueryKey, null)
      // Anything cached for the previous user must not leak into the next session.
      void queryClient.invalidateQueries({
        predicate: (query) => query.queryKey[0] !== sessionQueryKey[0],
      })
    }
  }, [queryClient])

  const hasRole = useCallback(
    (...roles: Role[]) => roles.some((role) => user?.roles.includes(role) ?? false),
    [user],
  )

  const value = useMemo<AuthContextValue>(
    () => ({ status, user, error: session.error, signIn, signOut, hasRole }),
    [status, user, session.error, signIn, signOut, hasRole],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}
