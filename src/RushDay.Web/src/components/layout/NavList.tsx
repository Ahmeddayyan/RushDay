import { NavLink } from 'react-router'

import { navItems } from '@/app/navigation'
import { useAuth } from '@/features/auth'
import { cn } from '@/lib/cn'

export interface NavListProps {
  /** Called after a link is activated; the mobile drawer uses it to close itself. */
  onNavigate?: () => void
}

/** The primary navigation list, shared by the desktop sidebar and the mobile drawer. */
export function NavList({ onNavigate }: NavListProps) {
  const { hasRole } = useAuth()
  const visible = navItems.filter((item) => !item.roles || hasRole(...item.roles))

  return (
    <ul className="flex flex-col gap-1">
      {visible.map((item) => {
        const Icon = item.icon
        return (
          <li key={item.to}>
            <NavLink
              to={item.to}
              end={item.end}
              onClick={onNavigate}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
                  isActive
                    ? 'bg-primary/12 text-primary'
                    : 'text-muted hover:bg-border/40 hover:text-text',
                )
              }
            >
              <Icon aria-hidden="true" className="size-4 shrink-0" />
              {item.label}
            </NavLink>
          </li>
        )
      })}
    </ul>
  )
}
