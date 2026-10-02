import { Compass, House, LogIn } from 'lucide-react'

import { useAuth } from '@/app/AuthProvider'
import { roleHome } from '@/components/layout/navItems'
import { ButtonLink } from '@/components/ui'
import { useDocumentTitle } from '@/lib/useDocumentTitle'

/** Any unknown path: the same shape as /forbidden. */
export function Component() {
  useDocumentTitle('Page not found · RushDay')
  const { user } = useAuth()

  return (
    <div className="flex min-h-[60dvh] items-center justify-center">
      <div className="flex flex-col items-center gap-3 px-6 py-14 text-center">
        <span
          aria-hidden="true"
          className="flex size-12 items-center justify-center rounded-full bg-surface-2 text-muted ring-1 ring-border"
        >
          <Compass className="size-6" />
        </span>
        <h1 tabIndex={-1} className="text-xl font-semibold text-text outline-none md:text-2xl">
          That page doesn&apos;t exist
        </h1>
        <p className="max-w-prose text-sm text-muted">
          Check the address for typos, or {user ? 'go to your home page' : 'sign in'} and find it
          from there.
        </p>
        <div className="mt-1">
          {user ? (
            <ButtonLink to={roleHome(user.role)} variant="secondary">
              <House aria-hidden="true" className="size-4" />
              Go to your home page
            </ButtonLink>
          ) : (
            <ButtonLink to="/login">
              <LogIn aria-hidden="true" className="size-4" />
              Sign in
            </ButtonLink>
          )}
        </div>
      </div>
    </div>
  )
}
