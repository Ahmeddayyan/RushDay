import { Link, Outlet } from 'react-router'

import { SkipLink } from '@/components/layout/SkipLink'
import { ThemeToggle } from '@/features/theme'

/** Minimal shell for sign-in and similar pages: no sidebar, content centred. */
export function AuthLayout() {
  return (
    <div className="flex min-h-dvh flex-col bg-background text-text">
      <SkipLink />
      <header className="flex h-14 items-center justify-between px-4 sm:px-6">
        <Link to="/" className="rounded-sm text-lg font-semibold tracking-tight">
          RushDay
        </Link>
        <ThemeToggle />
      </header>
      <main
        id="main"
        tabIndex={-1}
        className="flex flex-1 items-start justify-center px-4 py-8 outline-none sm:items-center"
      >
        <Outlet />
      </main>
    </div>
  )
}
