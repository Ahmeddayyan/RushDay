import { Outlet } from 'react-router'

import { Header } from '@/components/layout/Header'
import { Sidebar } from '@/components/layout/Sidebar'
import { SkipLink } from '@/components/layout/SkipLink'

/** Signed-in shell: sidebar on md+, header with drawer below. Pages render into <main>. */
export function RootLayout() {
  return (
    <div className="flex min-h-dvh bg-background text-text">
      <SkipLink />
      <Sidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <Header />
        <main id="main" tabIndex={-1} className="flex-1 px-4 py-6 outline-none sm:px-6 lg:px-8">
          <div className="mx-auto w-full max-w-6xl">
            <Outlet />
          </div>
        </main>
        <footer className="border-t border-border px-4 py-4 text-xs text-muted sm:px-6 lg:px-8">
          <div className="mx-auto flex w-full max-w-6xl flex-wrap gap-x-4 gap-y-1">
            <span>RushDay student portal</span>
            <a href="/api" className="hover:text-text">
              API index
            </a>
            <a href="/openapi/v1.json" className="hover:text-text">
              OpenAPI
            </a>
          </div>
        </footer>
      </div>
    </div>
  )
}
