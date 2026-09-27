import { Link } from 'react-router'

import { NavList } from './NavList'

/** Desktop navigation. Hidden below the md breakpoint, where <MobileNav> takes over. */
export function Sidebar() {
  return (
    <aside className="hidden w-64 shrink-0 flex-col border-r border-border bg-surface md:flex">
      <div className="flex h-14 items-center border-b border-border px-5">
        <Link to="/" className="rounded-sm text-lg font-semibold tracking-tight text-text">
          RushDay
        </Link>
      </div>
      <nav aria-label="Primary" className="flex-1 p-3">
        <NavList />
      </nav>
      <p className="border-t border-border px-5 py-4 text-xs text-muted">
        Student portal · results published 28 Sep 2026, 09:00
      </p>
    </aside>
  )
}
