import { House, LogIn } from 'lucide-react'
import { useLocation } from 'react-router'

import type { Role } from '@/api/types/common'
import { useAuth } from '@/app/AuthProvider'
import type { ForbiddenLocationState } from '@/app/guards'
import { roleHome } from '@/components/layout/navItems'
import { ButtonLink, ForbiddenState } from '@/components/ui'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

const audience: Record<Role, string> = {
  Student: 'students',
  Lecturer: 'lecturers',
  Admin: 'administrators',
}

const article: Record<Role, string> = {
  Student: 'a student',
  Lecturer: 'a lecturer',
  Admin: 'an administrator',
}

function isForbiddenState(value: unknown): value is ForbiddenLocationState {
  return (
    typeof value === 'object' &&
    value !== null &&
    Array.isArray((value as ForbiddenLocationState).roles)
  )
}

/** `/forbidden` (05-frontend.md section 10): who the page is for, and the way back. */
export function Component() {
  useDocumentTitle('No access · RushDay')
  const { user } = useAuth()
  const location = useLocation()
  const state: unknown = location.state
  const roles = isForbiddenState(state) ? state.roles : []

  const forWhom =
    roles.length > 0
      ? `That page is for ${roles.map((role) => audience[role]).join(' and ')}.`
      : 'That page is for a different role.'
  const who = user ? ` You're signed in as ${article[user.role]}.` : ''

  return (
    <div className="flex min-h-[60dvh] items-center justify-center">
      <ForbiddenState
        headingLevel="h1"
        description={
          <>
            {forWhom}
            {who} If you think you should have access, contact the academic office.
          </>
        }
        action={
          user ? (
            <ButtonLink to={roleHome(user.role)} variant="secondary">
              <House aria-hidden="true" className="size-4" />
              Go to your home page
            </ButtonLink>
          ) : (
            <ButtonLink to="/login">
              <LogIn aria-hidden="true" className="size-4" />
              Sign in
            </ButtonLink>
          )
        }
      />
    </div>
  )
}
