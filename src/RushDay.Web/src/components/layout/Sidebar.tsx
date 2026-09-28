import { NavLink } from 'react-router'

import { navItems, navLinkClassName } from './navItems'

/** Desktop navigation (>= 1024 px). Below that, <MobileDrawer> takes over. */
export function Sidebar() {
  return (
    <aside className="hidden w-64 shrink-0 flex-col border-r border-border bg-surface lg:flex">
      <div className="flex h-14 items-center border-b border-border px-5">
        <span className="text-lg font-semibold tracking-tight text-text">RushDay</span>
      </div>
      <nav aria-label="Primary" className="flex-1 p-3">
        <ul className="flex flex-col gap-1">
          {navItems.map(({ to, label, icon: Icon, end }) => (
            <li key={to}>
              <NavLink to={to} end={end} className={({ isActive }) => navLinkClassName(isActive)}>
                <Icon aria-hidden="true" className="size-4 shrink-0" />
                {label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
    </aside>
  )
}
