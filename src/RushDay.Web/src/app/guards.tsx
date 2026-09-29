import type { ReactNode } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router'

import type { Role } from '@/api/types/common'
import { roleHome } from '@/components/layout/navItems'
import { Wordmark } from '@/components/layout/Wordmark'
import { ColdStartNotice, ErrorState, Spinner } from '@/components/ui'
import { loginPathFor, sanitizeReturnTo } from '@/lib/returnTo'

import { useAuth } from './AuthProvider'
import { gatedDestination, gateRedirect } from './gates'

/**
 * Route guards (05-frontend.md section 5.2). Each is a pathless layout route that renders <Outlet />
 * when allowed:
 *
 * - `RootRedirect` (`/`): booting → splash; anonymous or mfaPending → /login; else the role home.
 * - `PublicOnly` (`/login`): a signed-in visitor goes to a safe `returnTo`, else the role home.
 * - `RequireAuth`: booting → splash; anonymous → /login?returnTo=…; then the two gates, in order:
 *   `mustChangePassword` → /account/password?required=1, then `mfaSetupRequired` →
 *   /account/mfa?required=1, each carrying the original destination as returnTo.
 * - `RequireRole`: the same, then a wrong role → /forbidden (a real page, never a redirect loop).
 */

/** The full-page splash while `GET /api/auth/me` is unresolved, so a signed-in user never sees the form flash. */
export function BootSplash() {
  const { boot } = useAuth()

  return (
    <div className="flex min-h-dvh flex-col items-center justify-center gap-6 bg-background px-4 py-10">
      <Wordmark size="lg" />
      {boot.failed ? (
        <div className="w-full max-w-md rounded-lg border border-border bg-surface shadow-card">
          <ErrorState
            title="The server isn't answering"
            description="We kept trying for over a minute. Check your connection, then try again."
            onRetry={boot.retry}
            headingLevel="h1"
            compact
          />
        </div>
      ) : (
        <div className="flex w-full max-w-sm flex-col items-center gap-4">
          <Spinner size="lg" label="Loading RushDay" />
          {boot.slow && <ColdStartNotice className="w-full" />}
        </div>
      )}
    </div>
  )
}

function useSignedInGuard(): ReactNode | null {
  const { status, user, endReason } = useAuth()
  const location = useLocation()

  if (status === 'booting') return <BootSplash />
  if (status !== 'authenticated' || !user) {
    // An explicit sign-out goes to a clean sign-in page; an expired session comes back afterwards.
    const target =
      endReason === 'logout' ? '/login' : loginPathFor(location.pathname, location.search)
    return <Navigate to={target} replace />
  }
  const gate = gateRedirect(user, location.pathname, location.search)
  if (gate) return <Navigate to={gate} replace />
  return null
}

export function RootRedirect() {
  const { status, user } = useAuth()
  if (status === 'booting') return <BootSplash />
  if (status !== 'authenticated' || !user) return <Navigate to="/login" replace />
  return <Navigate to={gatedDestination(user, roleHome(user.role))} replace />
}

export function PublicOnly() {
  const { status, user } = useAuth()
  const location = useLocation()

  if (status === 'booting') return <BootSplash />
  if (status === 'authenticated' && user) {
    const returnTo = sanitizeReturnTo(new URLSearchParams(location.search).get('returnTo'))
    return <Navigate to={gatedDestination(user, returnTo ?? roleHome(user.role))} replace />
  }
  return <Outlet />
}

export function RequireAuth() {
  const redirect = useSignedInGuard()
  return redirect ?? <Outlet />
}

export interface RequireRoleProps {
  roles: Role[]
}

/** State passed to /forbidden so the page can say who the area is for. */
export interface ForbiddenLocationState {
  from: string
  roles: Role[]
}

export function RequireRole({ roles }: RequireRoleProps) {
  const redirect = useSignedInGuard()
  const { user } = useAuth()
  const location = useLocation()

  if (redirect) return redirect
  if (!user || !roles.includes(user.role)) {
    const state: ForbiddenLocationState = { from: location.pathname, roles }
    return <Navigate to="/forbidden" replace state={state} />
  }
  return <Outlet />
}
