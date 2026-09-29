import { NavLink } from 'react-router'

import { usePublicStatus } from '@/api/endpoints/public'
import { formatSemester } from '@/lib/format'

import { navLinkClassName, type NavItem } from './navItems'
import { Wordmark } from './Wordmark'

export function NavList({ items, onNavigate }: { items: NavItem[]; onNavigate?: () => void }) {
  return (
    <ul className="flex flex-col gap-0.5">
      {items.map(({ to, label, icon: Icon, end }) => (
        <li key={to}>
          <NavLink
            to={to}
            end={end}
            onClick={onNavigate}
            className={({ isActive }) => navLinkClassName(isActive)}
          >
            <Icon aria-hidden="true" className="size-4 shrink-0" />
            {label}
          </NavLink>
        </li>
      ))}
    </ul>
  )
}

/**
 * Desktop navigation (from 1024 px), 264 px wide. Below that, <MobileDrawer> takes over. The column
 * (background and border) stretches the full height of the page; its content is sticky, so it stays
 * in view while the page scrolls and a full-page capture or a print shows one continuous column.
 */
export function Sidebar({ items }: { items: NavItem[] }) {
  const { data: status } = usePublicStatus()

  return (
    <aside className="hidden w-[264px] shrink-0 border-r border-border bg-surface lg:block">
      <div className="sticky top-0 flex h-dvh flex-col">
        <div className="flex h-16 shrink-0 items-center border-b border-border px-5">
          <Wordmark to="/" />
        </div>
        <nav aria-label="Primary" className="flex-1 overflow-y-auto px-3 py-4">
          <NavList items={items} />
        </nav>
        {status && (
          <div className="border-t border-border px-5 py-4 text-xs text-muted">
            <p className="font-medium text-text">{status.institution.name}</p>
            <p>
              {status.academicYear} · {formatSemester(status.currentSemester)} semester
            </p>
          </div>
        )}
      </div>
    </aside>
  )
}
