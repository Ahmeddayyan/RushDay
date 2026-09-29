import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { useQueryClient } from '@tanstack/react-query'

import { ApiError, configureClient, type ClientHooks } from '@/api/client'
import * as authApi from '@/api/endpoints/auth'
import { queryKeys } from '@/api/keys'
import type { Me } from '@/api/types/common'
import { openAuthChannel, type AuthChannel } from '@/lib/broadcast'
import { toast } from '@/lib/toast'
import { hasDirtyForms } from '@/lib/useDirtyForm'

import { ReauthDialog } from './ReauthDialog'

/**
 * The session state machine (05-frontend.md section 5.1).
 *
 *   booting ──200──▶ authenticated ◀──verify── mfaPending
 *      │                  ▲   │                     ▲
 *      └──401──▶ anonymous ┴───┘ logout / expiry     │
 *                    └──────── login (MfaChallenge) ─┘
 *
 * - Boot: `GET /api/auth/me`. 200 → authenticated; 401 → anonymous plus an anonymous CSRF token
 *   from `GET /api/auth/csrf`; a network error, 5xx or 429 keeps booting and retries after 1, 2, 4,
 *   8, 8, … s. After 3 s the splash shows the cold-start notice; after 75 s an error with Retry.
 * - The CSRF request token lives in memory only (a ref for the client, state for React).
 * - A 401 from any other request ends the session with one toast, unless a form has unsaved work
 *   (`useDirtyForm`), in which case ReauthDialog asks for the password again and the page stays.
 * - Logout clears the query cache and tells the other tabs over BroadcastChannel.
 */

export type AuthStatus = 'booting' | 'anonymous' | 'mfaPending' | 'authenticated'

/** Why the last session ended: guards send `logout` to /login and `expired` to /login?returnTo=…. */
export type SessionEndReason = 'logout' | 'expired' | null

export type LoginResult = 'authenticated' | 'mfaRequired'

export interface BootState {
  /** More than 3 s without an answer: show ColdStartNotice. */
  slow: boolean
  /** 75 s without an answer: show an error with Retry. */
  failed: boolean
  retry: () => void
}

export interface AuthContextValue {
  status: AuthStatus
  user: Me | null
  csrf: string | null
  boot: BootState
  endReason: SessionEndReason
  /** `POST /api/auth/login`; resolves 'mfaRequired' when a code step follows. Errors are thrown. */
  login: (username: string, password: string) => Promise<LoginResult>
  /** `POST /api/auth/mfa/verify`; a wrong code throws and keeps `mfaPending`. */
  verifyMfa: (code: string) => Promise<void>
  /** "Start again" from the code step. */
  cancelMfa: () => void
  logout: () => Promise<void>
  /** Refetches `/api/auth/me` (after a password change or MFA setup); null when the session ended. */
  refreshUser: () => Promise<Me | null>
  /** Adopts a fresh `Me` returned by another auth call (`POST /api/auth/mfa/enable`). */
  completeSession: (me: Me) => void
}

export const BOOT_SLOW_MS = 3_000
export const BOOT_GIVE_UP_MS = 75_000
const BOOT_MAX_DELAY_MS = 8_000
const SESSION_ENDED_TOAST = 'session-ended'

const AuthContext = createContext<AuthContextValue | null>(null)

// The provider and its hook belong together; a full reload on edit is an acceptable trade.
// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside <AuthProvider>.')
  return context
}

export interface AuthProviderProps {
  children: ReactNode
  /**
   * Test seam: start from a known session instead of booting through `GET /api/auth/me`
   * (`renderWithProviders({ user })`). Null user means signed out.
   */
  initialSession?: { user: Me | null; csrf?: string | null }
}

export function AuthProvider({ children, initialSession }: AuthProviderProps) {
  const queryClient = useQueryClient()
  const primed = initialSession !== undefined
  const initialStatus: AuthStatus = !primed
    ? 'booting'
    : initialSession.user
      ? 'authenticated'
      : 'anonymous'
  const initialCsrf = initialSession?.csrf ?? initialSession?.user?.csrfToken ?? null

  const [status, setStatusState] = useState<AuthStatus>(initialStatus)
  const [user, setUser] = useState<Me | null>(initialSession?.user ?? null)
  const [csrf, setCsrfState] = useState<string | null>(initialCsrf)
  const [endReason, setEndReason] = useState<SessionEndReason>(null)
  const [bootSlow, setBootSlow] = useState(false)
  const [bootFailed, setBootFailed] = useState(false)
  const [bootAttempt, setBootAttempt] = useState(0)
  const [reauthOpen, setReauthOpenState] = useState(false)

  // Refs mirror what the client hooks read outside React's render cycle.
  const statusRef = useRef<AuthStatus>(initialStatus)
  const csrfRef = useRef<string | null>(initialCsrf)
  const reauthOpenRef = useRef(false)
  const loggingOutRef = useRef(false)
  const channelRef = useRef<AuthChannel | null>(null)

  const setStatus = useCallback((next: AuthStatus) => {
    statusRef.current = next
    setStatusState(next)
  }, [])

  const setCsrf = useCallback((token: string | null) => {
    csrfRef.current = token
    setCsrfState(token)
  }, [])

  const setReauthOpen = useCallback((open: boolean) => {
    reauthOpenRef.current = open
    setReauthOpenState(open)
  }, [])

  const fetchAnonymousToken = useCallback(async () => {
    try {
      const { csrfToken } = await authApi.getCsrf()
      setCsrf(csrfToken)
    } catch {
      // No token yet: the first POST answers 400 antiforgery, and the client refreshes and retries.
      setCsrf(null)
    }
  }, [setCsrf])

  const applyMe = useCallback(
    (me: Me) => {
      setUser(me)
      setCsrf(me.csrfToken)
      setEndReason(null)
      setStatus('authenticated')
      queryClient.setQueryData(queryKeys.authMe, me)
    },
    [queryClient, setCsrf, setStatus],
  )

  const endSession = useCallback(
    (reason: Exclude<SessionEndReason, null>) => {
      queryClient.clear()
      setReauthOpen(false)
      setUser(null)
      setEndReason(reason)
      setStatus('anonymous')
      void fetchAnonymousToken()
    },
    [fetchAnonymousToken, queryClient, setReauthOpen, setStatus],
  )

  const expireSession = useCallback(() => {
    endSession('expired')
    toast.info('Your session has ended. Sign in again.', { id: SESSION_ENDED_TOAST })
  }, [endSession])

  const handleUnauthenticated = useCallback(() => {
    if (loggingOutRef.current || statusRef.current !== 'authenticated' || reauthOpenRef.current)
      return
    if (hasDirtyForms()) {
      setReauthOpen(true)
      return
    }
    expireSession()
  }, [expireSession, setReauthOpen])

  const refreshUser = useCallback(async (): Promise<Me | null> => {
    try {
      const me = await authApi.getMe()
      applyMe(me)
      return me
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        if (statusRef.current === 'authenticated') expireSession()
        return null
      }
      throw error
    }
  }, [applyMe, expireSession])

  const refreshCsrfToken = useCallback(async () => {
    if (statusRef.current === 'authenticated' && !reauthOpenRef.current) {
      try {
        applyMe(await authApi.getMe())
        return
      } catch (error) {
        if (!(error instanceof ApiError && error.status === 401)) throw error
        handleUnauthenticated()
      }
    }
    const { csrfToken } = await authApi.getCsrf()
    setCsrf(csrfToken)
  }, [applyMe, handleUnauthenticated, setCsrf])

  const hooks = useMemo<ClientHooks>(
    () => ({
      getCsrfToken: () => csrfRef.current,
      refreshCsrfToken,
      onUnauthenticated: handleUnauthenticated,
      onGateRequired: () => {
        void refreshUser().catch(() => {})
      },
    }),
    [handleUnauthenticated, refreshCsrfToken, refreshUser],
  )

  // A layout effect runs before any passive effect, so the client is configured before the first
  // query of any child fetches.
  useLayoutEffect(() => {
    configureClient(hooks)
  }, [hooks])

  // Boot: GET /api/auth/me with backoff while the server is cold, asleep or shedding.
  useEffect(() => {
    if (primed && bootAttempt === 0) return
    let cancelled = false
    let retryTimer: ReturnType<typeof setTimeout> | undefined
    let attempt = 0
    const started = Date.now()
    const slowTimer = setTimeout(() => setBootSlow(true), BOOT_SLOW_MS)
    const giveUpTimer = setTimeout(() => {
      cancelled = true
      clearTimeout(retryTimer)
      setBootFailed(true)
    }, BOOT_GIVE_UP_MS)

    const finish = () => {
      clearTimeout(slowTimer)
      clearTimeout(giveUpTimer)
    }

    const run = async () => {
      try {
        const me = await authApi.getMe()
        if (cancelled) return
        finish()
        applyMe(me)
      } catch (error) {
        if (cancelled) return
        if (error instanceof ApiError && error.status === 401) {
          finish()
          setStatus('anonymous')
          void fetchAnonymousToken()
          return
        }
        const delay = Math.min(1000 * 2 ** attempt, BOOT_MAX_DELAY_MS)
        attempt += 1
        if (Date.now() - started + delay >= BOOT_GIVE_UP_MS) return
        retryTimer = setTimeout(() => void run(), delay)
      }
    }

    void run()
    return () => {
      cancelled = true
      clearTimeout(retryTimer)
      finish()
    }
  }, [applyMe, bootAttempt, fetchAnonymousToken, primed, setStatus])

  const retryBoot = useCallback(() => {
    setBootFailed(false)
    setBootSlow(false)
    setBootAttempt((value) => value + 1)
  }, [])

  // Other tabs: a sign-out anywhere signs this tab out too.
  useEffect(() => {
    const channel = openAuthChannel((message) => {
      if (message.type !== 'logout') return
      if (statusRef.current === 'authenticated' || statusRef.current === 'mfaPending')
        endSession('logout')
    })
    channelRef.current = channel
    return () => {
      channel.close()
      channelRef.current = null
    }
  }, [endSession])

  const login = useCallback(
    async (username: string, password: string): Promise<LoginResult> => {
      const response = await authApi.login({ username, password })
      if (authApi.isMfaChallenge(response)) {
        setCsrf(response.csrfToken)
        setStatus('mfaPending')
        return 'mfaRequired'
      }
      applyMe(response)
      return 'authenticated'
    },
    [applyMe, setCsrf, setStatus],
  )

  const verifyMfa = useCallback(
    async (code: string) => {
      applyMe(await authApi.verifyMfa(code))
    },
    [applyMe],
  )

  const cancelMfa = useCallback(() => {
    if (statusRef.current === 'mfaPending') setStatus('anonymous')
  }, [setStatus])

  const logout = useCallback(async () => {
    loggingOutRef.current = true
    try {
      await authApi.logout()
    } catch (error) {
      // 401: the session had already ended, which is what signing out wants anyway.
      if (!(error instanceof ApiError && error.status === 401)) {
        loggingOutRef.current = false
        throw error
      }
    }
    channelRef.current?.post({ type: 'logout' })
    endSession('logout')
    loggingOutRef.current = false
  }, [endSession])

  const onReauthenticated = useCallback(
    (me: Me) => {
      applyMe(me)
      setReauthOpen(false)
      toast.success('Signed in again. Press Save to keep your changes.')
      void queryClient.refetchQueries({
        type: 'active',
        predicate: (query) => query.state.status === 'error',
      })
    },
    [applyMe, queryClient, setReauthOpen],
  )

  const onReauthCancelled = useCallback(() => {
    setReauthOpen(false)
    expireSession()
  }, [expireSession, setReauthOpen])

  const boot = useMemo<BootState>(
    () => ({ slow: bootSlow, failed: bootFailed, retry: retryBoot }),
    [bootFailed, bootSlow, retryBoot],
  )

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      csrf,
      boot,
      endReason,
      login,
      verifyMfa,
      cancelMfa,
      logout,
      refreshUser,
      completeSession: applyMe,
    }),
    [
      applyMe,
      boot,
      cancelMfa,
      csrf,
      endReason,
      login,
      logout,
      refreshUser,
      status,
      user,
      verifyMfa,
    ],
  )

  return (
    <AuthContext value={value}>
      {children}
      {reauthOpen && user && (
        <ReauthDialog
          username={user.username}
          onSignedIn={onReauthenticated}
          onCancel={onReauthCancelled}
          onToken={setCsrf}
        />
      )}
    </AuthContext>
  )
}
