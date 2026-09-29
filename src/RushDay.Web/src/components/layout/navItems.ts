import {
  Activity,
  BookOpen,
  CalendarClock,
  CalendarDays,
  GraduationCap,
  House,
  IdCard,
  KeyRound,
  LayoutDashboard,
  Library,
  Megaphone,
  ScrollText,
  Settings,
  UsersRound,
  type LucideIcon,
} from 'lucide-react'

import type { Role } from '@/api/types/common'
import { cn } from '@/lib/cn'

export interface NavItem {
  to: string
  label: string
  icon: LucideIcon
  /** Match the path exactly (area home pages share their prefix with every other page). */
  end: boolean
}

/** Navigation per role (05-frontend.md section 7), shared by the sidebar, the drawer and the bottom tabs. */
export const navItemsByRole: Record<Role, NavItem[]> = {
  Student: [
    { to: '/student', label: 'Home', icon: House, end: true },
    { to: '/student/results', label: 'Results', icon: GraduationCap, end: false },
    { to: '/student/timetable', label: 'Timetable', icon: CalendarDays, end: false },
    { to: '/student/modules', label: 'Modules', icon: BookOpen, end: false },
    { to: '/announcements', label: 'Announcements', icon: Megaphone, end: false },
  ],
  Lecturer: [
    { to: '/lecturer', label: 'Home', icon: House, end: true },
    { to: '/lecturer/modules', label: 'My modules', icon: Library, end: false },
    { to: '/announcements', label: 'Announcements', icon: Megaphone, end: false },
  ],
  Admin: [
    { to: '/admin', label: 'Overview', icon: LayoutDashboard, end: true },
    { to: '/admin/students', label: 'Students', icon: UsersRound, end: false },
    { to: '/admin/modules', label: 'Modules', icon: BookOpen, end: false },
    { to: '/admin/lecturers', label: 'Lecturers', icon: IdCard, end: false },
    { to: '/admin/accounts', label: 'Accounts', icon: KeyRound, end: false },
    { to: '/admin/enrolment', label: 'Enrolment windows', icon: CalendarClock, end: false },
    { to: '/admin/results', label: 'Results', icon: GraduationCap, end: false },
    { to: '/admin/announcements', label: 'Announcements', icon: Megaphone, end: false },
    { to: '/admin/audit', label: 'Audit log', icon: ScrollText, end: false },
    { to: '/admin/ops', label: 'Operations', icon: Activity, end: false },
    { to: '/admin/settings', label: 'Settings', icon: Settings, end: false },
  ],
}

/** Students' bottom tabs below 768 px: Home, Results, Timetable, Modules. */
export const studentBottomTabs: NavItem[] = navItemsByRole.Student.slice(0, 4)

/** The signed-in person's home page for each role (RootRedirect, PublicOnly, "Go to your home page"). */
export function roleHome(role: Role | undefined | null): string {
  switch (role) {
    case 'Student':
      return '/student'
    case 'Lecturer':
      return '/lecturer'
    case 'Admin':
      return '/admin'
    default:
      return '/login'
  }
}

export function navLinkClassName(isActive: boolean): string {
  return cn(
    'flex min-h-10 items-center gap-3 rounded-md px-3 text-sm font-medium transition-colors pointer-coarse:min-h-11',
    isActive ? 'bg-primary-soft text-primary' : 'text-muted hover:bg-surface-2 hover:text-text',
  )
}
