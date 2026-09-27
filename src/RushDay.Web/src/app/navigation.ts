import {
  BookOpen,
  LayoutDashboard,
  Megaphone,
  Settings,
  Users,
  type LucideIcon,
} from 'lucide-react'

import type { Role } from '@/features/auth'

export interface NavItem {
  to: string
  label: string
  icon: LucideIcon
  /** Shown only to users holding one of these roles. Omit for everyone. */
  roles?: Role[]
  /** Match this path exactly (used for "/" so it is not active on every page). */
  end?: boolean
}

export const navItems: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, end: true },
  { to: '/modules', label: 'Modules', icon: BookOpen },
  { to: '/announcements', label: 'Announcements', icon: Megaphone },
  { to: '/staff', label: 'Staff', icon: Users, roles: ['Staff', 'Admin'] },
  { to: '/admin', label: 'Admin', icon: Settings, roles: ['Admin'] },
]
