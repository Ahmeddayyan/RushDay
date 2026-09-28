import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react'

import { ApiError, apiFetch, configureClient } from '@/api/client'
import type { Role } from '@/api/types/auth'

/**
 * Session state machine (05-frontend.md section 5.1). This is the S3 skeleton: the boot sequence,
 * login, MFA verification and logout are wired to the real endpoints, but the retry-with-backoff
 * cold-start handling, the antiforgery-refresh-and-retry and ReauthDialog wiring are stage S5's job.
 */
export type AuthStatus = 'booting' | 'anonymous' | 'mfaPending' | 'authenticated'

/**
 * The session shape the SPA needs today. This is a working subset kept local to stage S3; the full
 * `Me` contract of 02-api.md section 7 lands in `api/types/common.ts` in stage S5.
 */
export interface Me {
  id: string
  username: string
  displayName: string
  role: Role
  csrfToken: string
  mustChangePassword: boolean
  mfaSetupRequired: boolean
}

export interface AuthContextValue {
  status: AuthStatus
  user: Me | null
  csrf: string | null
  login: (username: string, password: string) => Promise<void>
  verifyMfa: (code: string) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

// This module pairs a provider component with its hook, so Vite's fast-refresh boundary is not
// pure-component; a full reload on edit here is an acceptable trade for keeping the two together.
// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside <AuthProvider>.')
  return context
}

interface MfaChallenge {
  mfaRequired: true
  csrfToken: string
}

function isMfaChallenge(value: unknown): value is MfaChallenge {
  return (
    typeof value === 'object' &&
    value !== null &&
    (value as Partial<MfaChallenge>).mfaRequired === true
  )
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('booting')
  const [user, setUser] = useState<Me | null>(null)
  const [csrf, setCsrfState] = useState<string | null>(null)
  const csrfRef = useRef<string | null>(null)

  const setCsrf = useCallback((token: string | null) => {
    csrfRef.current = token
    setCsrfState(token)
  }, [])

  // configureClient is called once on mount, per the client's contract (05-frontend.md section 5.3).
  useEffect(() => {
    configureClient({
      getCsrfToken: () => csrfRef.current,
      refreshCsrfToken: async () => {
        const response = await apiFetch<{ token: string }>('/api/auth/csrf')
        setCsrf(response.token)
      },
      onUnauthenticated: () => {
        setStatus('anonymous')
        setUser(null)
        setCsrf(null)
      },
    })
  }, [setCsrf])

  useEffect(() => {
    let cancelled = false

    async function boot() {
      try {
        const me = await apiFetch<Me>('/api/auth/me')
        if (cancelled) return
        setUser(me)
        setCsrf(me.csrfToken)
        setStatus('authenticated')
      } catch (error) {
        if (cancelled) return
        if (error instanceof ApiError && error.status === 401) {
          try {
            const response = await apiFetch<{ token: string }>('/api/auth/csrf')
            if (!cancelled) setCsrf(response.token)
          } catch {
            // Stage S5 adds the retry-with-backoff and cold-start notice for this path.
          }
        }
        if (!cancelled) setStatus('anonymous')
      }
    }

    void boot()
    return () => {
      cancelled = true
    }
  }, [setCsrf])

  const login = useCallback(
    async (username: string, password: string) => {
      const response = await apiFetch<Me | MfaChallenge>('/api/auth/login', {
        method: 'POST',
        body: { username, password },
      })
      if (isMfaChallenge(response)) {
        setCsrf(response.csrfToken)
        setStatus('mfaPending')
        return
      }
      setUser(response)
      setCsrf(response.csrfToken)
      setStatus('authenticated')
    },
    [setCsrf],
  )

  const verifyMfa = useCallback(
    async (code: string) => {
      const me = await apiFetch<Me>('/api/auth/mfa/verify', { method: 'POST', body: { code } })
      setUser(me)
      setCsrf(me.csrfToken)
      setStatus('authenticated')
    },
    [setCsrf],
  )

  const logout = useCallback(async () => {
    await apiFetch<void>('/api/auth/logout', { method: 'POST', expect: 'void' })
    setUser(null)
    setStatus('anonymous')
    try {
      const response = await apiFetch<{ token: string }>('/api/auth/csrf')
      setCsrf(response.token)
    } catch {
      setCsrf(null)
    }
  }, [setCsrf])

  const value: AuthContextValue = { status, user, csrf, login, verifyMfa, logout }

  return <AuthContext value={value}>{children}</AuthContext>
}
