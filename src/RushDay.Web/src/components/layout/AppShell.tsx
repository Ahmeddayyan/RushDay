import { useEffect, useRef } from 'react'
import { Outlet, useLocation } from 'react-router'

import { useAuth } from '@/app/AuthProvider'
import { cn } from '@/lib/cn'

import { BottomTabs } from './BottomTabs'
import { Footer } from './Footer'
import { navItemsByRole, studentBottomTabs } from './navItems'
import { Sidebar } from './Sidebar'
import { SkipLink } from './SkipLink'
import { TopBar } from './TopBar'

export interface AppShellProps {
  /** 1400 px content width for admin tables and the ops page (1200 px otherwise). */
  wide?: boolean
}

/**
 * The page frame (05-frontend.md sections 7, 9.2 and 12): skip link, 264 px sidebar from 1024 px,
 * top bar with the drawer below that, students' bottom tabs below 768 px, `<main id="main">` and the
 * footer. Used as a pathless layout route:
 *
 *   { element: <RequireRole roles={['Student']} />, children: [
 *       { element: <AppShell />, children: [{ path: '/student', lazy: () => import('./StudentDashboardPage') }] } ] }
 *
 * While a gate is in force (forced password change or MFA setup, or `?required=1`) navigation is
 * hidden and only Sign out remains; visitors who are not signed in (public pages) get the wordmark,
 * the theme toggle and Sign in. After each navigation, focus moves to the page's `<h1>`.
 */
export function AppShell({ wide = false }: AppShellProps) {
  const { status, user } = useAuth()
  const location = useLocation()
  const mainRef = useRef<HTMLElement>(null)
  const previousPath = useRef(location.pathname)

  const signedIn = status === 'authenticated' && user !== null
  const requiredMode = new URLSearchParams(location.search).get('required') === '1'
  const gated = signedIn && (user.mustChangePassword || user.mfaSetupRequired || requiredMode)
  const navigation = signedIn && !gated
  const items = navigation ? navItemsByRole[user.role] : []
  const bottomTabs = navigation && user.role === 'Student'

  // Route change: back to the top, focus on the new page's heading (the first render keeps focus).
  useEffect(() => {
    if (previousPath.current === location.pathname) return
    previousPath.current = location.pathname
    document.documentElement.scrollTop = 0
    const main = mainRef.current
    const heading = main?.querySelector<HTMLElement>('h1')
    ;(heading ?? main)?.focus({ preventScroll: true })
  }, [location.pathname])

  return (
    <div className="flex min-h-dvh bg-background text-text">
      <SkipLink />
      {navigation && <Sidebar items={items} />}
      <div className={cn('flex min-h-dvh min-w-0 flex-1 flex-col', bottomTabs && 'max-md:pb-16')}>
        <TopBar items={items} navigation={navigation} gated={gated} />
        <main
          ref={mainRef}
          id="main"
          tabIndex={-1}
          className="flex-1 px-4 py-6 outline-none md:px-6 md:py-8 xl:px-8"
        >
          <div className={cn('mx-auto w-full', wide ? 'max-w-[1400px]' : 'max-w-[1200px]')}>
            <Outlet />
          </div>
        </main>
        <Footer />
      </div>
      {bottomTabs && <BottomTabs items={studentBottomTabs} />}
    </div>
  )
}
