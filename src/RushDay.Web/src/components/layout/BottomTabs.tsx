import { NavLink } from 'react-router'

import { cn } from '@/lib/cn'

import type { NavItem } from './navItems'

/** Students' bottom tab bar below 768 px (05-frontend.md section 7): four thumb-reachable targets. */
export function BottomTabs({ items }: { items: NavItem[] }) {
  return (
    <nav
      aria-label="Shortcuts"
      className="fixed inset-x-0 bottom-0 z-30 border-t border-border bg-surface/95 pb-[env(safe-area-inset-bottom)] backdrop-blur md:hidden"
    >
      <ul
        className="grid"
        style={{ gridTemplateColumns: `repeat(${items.length}, minmax(0, 1fr))` }}
      >
        {items.map(({ to, label, icon: Icon, end }) => (
          <li key={to}>
            <NavLink
              to={to}
              end={end}
              className={({ isActive }) =>
                cn(
                  'flex h-16 flex-col items-center justify-center gap-1 text-xs font-medium transition-colors',
                  isActive ? 'text-primary' : 'text-muted hover:text-text',
                )
              }
            >
              <Icon aria-hidden="true" className="size-5" />
              {label}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}
