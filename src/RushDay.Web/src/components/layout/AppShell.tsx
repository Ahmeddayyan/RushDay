import { Outlet } from 'react-router'

import { Sidebar } from './Sidebar'
import { SkipLink } from './SkipLink'
import { TopBar } from './TopBar'

/**
 * Signed-in shell: sidebar on lg+, top bar with a drawer below. Not yet wired into the router
 * (student/lecturer/admin routes are empty until S7-S10); kept here as the S3 migration target for
 * the old RootLayout so it compiles ready for those stages to use.
 */
export function AppShell() {
  return (
    <div className="flex min-h-dvh bg-background text-text">
      <SkipLink />
      <Sidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <TopBar />
        <main id="main" tabIndex={-1} className="flex-1 px-4 py-6 outline-none sm:px-6 lg:px-8">
          <div className="mx-auto w-full max-w-6xl">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  )
}
