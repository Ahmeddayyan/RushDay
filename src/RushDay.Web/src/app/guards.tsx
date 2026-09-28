import { Navigate, Outlet, useLocation } from 'react-router'

import type { Role } from '@/api/types/auth'

import { useAuth } from './AuthProvider'

/**
 * Route guards (05-frontend.md section 5.2). This is the S3 skeleton: the redirect shape and role
 * checks are final; the returnTo sanitiser (`lib/returnTo.ts`) and the password/MFA gates are added
 * in stage S5.
 */

function BootingSplash() {
  return (
    <div role="status" className="flex min-h-dvh items-center justify-center text-sm text-muted">
      Loading…
    </div>
  )
}

function roleHome(role: Role | undefined): string {
  switch (role) {
    case 'Student':
      return '/student'
    case 'Lecturer':
      return '/lecturer'
    case 'Admin':
      return '/admin'
    default:
      return '/login'
  }
}

function returnPath(pathname: string, search: string): string {
  return `/login?returnTo=${encodeURIComponent(pathname + search)}`
}

/** The `/` route: sends every visitor somewhere real. */
export function RootRedirect() {
  const { status, user } = useAuth()

  if (status === 'booting') return <BootingSplash />
  if (status === 'anonymous' || status === 'mfaPending') return <Navigate to="/login" replace />
  return <Navigate to={roleHome(user?.role)} replace />
}

/** `/login`: a signed-in user is sent to a safe destination instead of seeing the form again. */
export function PublicOnly() {
  const { status, user } = useAuth()

  if (status === 'booting') return <BootingSplash />
  if (status === 'authenticated') return <Navigate to={roleHome(user?.role)} replace />
  return <Outlet />
}

/** Any signed-in route with no role restriction. */
export function RequireAuth() {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'booting') return <BootingSplash />
  if (status === 'anonymous' || status === 'mfaPending') {
    return <Navigate to={returnPath(location.pathname, location.search)} replace />
  }
  return <Outlet />
}

export interface RequireRoleProps {
  roles: Role[]
}

/** A role-restricted route: wrong role gets a real, assertable refusal page rather than a redirect loop. */
export function RequireRole({ roles }: RequireRoleProps) {
  const { status, user } = useAuth()
  const location = useLocation()

  if (status === 'booting') return <BootingSplash />
  if (status === 'anonymous' || status === 'mfaPending') {
    return <Navigate to={returnPath(location.pathname, location.search)} replace />
  }
  if (!user || !roles.includes(user.role)) return <Navigate to="/forbidden" replace />
  return <Outlet />
}
