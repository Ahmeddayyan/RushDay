import { Link } from 'react-router'

import { ThemeToggle } from '@/features/theme'

import { MobileNav } from './MobileNav'
import { SessionControls } from './SessionControls'

export function Header() {
  return (
    <header className="sticky top-0 z-30 border-b border-border bg-surface/95 backdrop-blur supports-[backdrop-filter]:bg-surface/80">
      <div className="flex h-14 items-center gap-2 px-3 sm:px-6 lg:px-8">
        <MobileNav />
        <Link
          to="/"
          className="rounded-sm text-lg font-semibold tracking-tight text-text md:hidden"
        >
          RushDay
        </Link>
        <div className="ml-auto flex items-center gap-1 sm:gap-2">
          <ThemeToggle />
          <SessionControls />
        </div>
      </div>
    </header>
  )
}
