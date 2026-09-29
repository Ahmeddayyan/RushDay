import type { ComponentProps, ReactNode } from 'react'
import * as RadixTabs from '@radix-ui/react-tabs'
import { NavLink } from 'react-router'

import { cn } from '@/lib/cn'

/**
 * In-page tabs on Radix (roving focus, arrow keys, `tablist`/`tab`/`tabpanel` roles), for example
 * the Chart | Table toggle of a chart card. Tabs that are separate URLs (a module's Roster | Marks |
 * Announcements) use <TabNav> instead, so each tab is a link that can be bookmarked and opened.
 */
export const Tabs = RadixTabs.Root

export function TabsList({ className, ...props }: ComponentProps<typeof RadixTabs.List>) {
  return (
    <RadixTabs.List
      className={cn(
        'inline-flex max-w-full items-center gap-1 overflow-x-auto rounded-lg bg-surface-2 p-1',
        className,
      )}
      {...props}
    />
  )
}

export function TabsTrigger({ className, ...props }: ComponentProps<typeof RadixTabs.Trigger>) {
  return (
    <RadixTabs.Trigger
      className={cn(
        'inline-flex h-8 cursor-pointer items-center gap-2 rounded-md px-3 text-sm font-medium whitespace-nowrap text-muted',
        'hover:text-text data-[state=active]:bg-surface data-[state=active]:text-text data-[state=active]:shadow-card',
        'pointer-coarse:h-11',
        className,
      )}
      {...props}
    />
  )
}

export function TabsContent({ className, ...props }: ComponentProps<typeof RadixTabs.Content>) {
  return <RadixTabs.Content className={cn('mt-4 outline-none', className)} {...props} />
}

export interface TabNavItem {
  to: string
  label: ReactNode
  /** Match the path exactly (the first tab usually shares its prefix with the others). */
  end?: boolean
}

/** Route tabs: a labelled <nav> of links with `aria-current="page"` on the active one. */
export function TabNav({
  items,
  label,
  className,
}: {
  items: TabNavItem[]
  label: string
  className?: string
}) {
  return (
    <nav aria-label={label} className={cn('border-b border-border', className)}>
      <ul className="-mb-px flex gap-1 overflow-x-auto">
        {items.map((item) => (
          <li key={item.to}>
            <NavLink
              to={item.to}
              end={item.end ?? false}
              className={({ isActive }) =>
                cn(
                  'inline-flex h-10 items-center border-b-2 px-3 text-sm font-medium whitespace-nowrap pointer-coarse:h-11',
                  isActive
                    ? 'border-primary text-primary'
                    : 'border-transparent text-muted hover:text-text',
                )
              }
            >
              {item.label}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}
