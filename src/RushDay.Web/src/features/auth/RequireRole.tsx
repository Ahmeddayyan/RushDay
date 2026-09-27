import type { ReactNode } from 'react'
import { ShieldCheck } from 'lucide-react'
import { Navigate, useLocation } from 'react-router'

import { ErrorState } from '@/components/ui/ErrorState'
import { Spinner } from '@/components/ui/Spinner'

import { useAuth } from './AuthContext'
import type { Role } from './types'

export interface RequireRoleProps {
  /** Any one of these roles grants access. Omit to require only a signed-in user. */
  roles?: Role[]
  children: ReactNode
}

/** Route guard: anonymous users go to /login (and come back afterwards); wrong role gets a clear refusal. */
export function RequireRole({ roles, children }: RequireRoleProps) {
  const { status, hasRole } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return (
      <div className="flex justify-center py-16">
        <Spinner label="Checking your session" />
      </div>
    )
  }

  if (status === 'anonymous') {
    const from = `${location.pathname}${location.search}`
    return <Navigate to="/login" replace state={{ from }} />
  }

  if (roles && roles.length > 0 && !hasRole(...roles)) {
    return (
      <ErrorState
        icon={ShieldCheck}
        title="You do not have access to this page"
        description={`This area is for ${formatRoles(roles)}. If you think that is wrong, contact the portal team.`}
      />
    )
  }

  return children
}

function formatRoles(roles: Role[]): string {
  const names = roles.map((role) => role.toLowerCase())
  if (names.length === 1) return names[0]
  return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}
