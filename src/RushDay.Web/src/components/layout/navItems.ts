import { Home, Megaphone, UserRound, type LucideIcon } from 'lucide-react'

import { cn } from '@/lib/cn'

export interface NavItem {
  to: string
  label: string
  icon: LucideIcon
  end: boolean
}

/**
 * A minimal, role-agnostic placeholder list for stage S3, shared by <Sidebar> and <MobileDrawer>.
 * Stages S7-S10 replace this with the real per-role navigation of 05-frontend.md section 7 once
 * their areas exist.
 */
export const navItems: NavItem[] = [
  { to: '/', label: 'Home', icon: Home, end: true },
  { to: '/announcements', label: 'Announcements', icon: Megaphone, end: false },
  { to: '/account', label: 'Account', icon: UserRound, end: false },
]

export function navLinkClassName(isActive: boolean): string {
  return cn(
    'flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium',
    isActive ? 'bg-primary/12 text-primary' : 'text-muted hover:bg-border/40 hover:text-text',
  )
}
