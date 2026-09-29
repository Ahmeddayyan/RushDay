import type { RouteObject } from 'react-router'

import { ErrorBoundary } from '@/app/ErrorBoundary'
import { BootSplash, RequireRole } from '@/app/guards'
import { AppShell } from '@/components/layout/AppShell'

/**
 * The administrator area (05-frontend.md sections 3, 7 and 10). Imported statically by
 * app/router.tsx; every page is lazy, so a student or lecturer never downloads admin code and the
 * area stays out of the entry bundle. `AppShell wide` gives the admin tables 1400 px. `/admin/ops`
 * belongs to stage S10 (features/ops/routes.tsx).
 */
export const adminRoutes: RouteObject[] = [
  {
    element: <RequireRole roles={['Admin']} />,
    errorElement: <ErrorBoundary />,
    hydrateFallbackElement: <BootSplash />,
    children: [
      {
        element: <AppShell wide />,
        children: [
          { path: '/admin', lazy: () => import('./AdminOverviewPage') },
          { path: '/admin/students', lazy: () => import('./StudentsPage') },
          { path: '/admin/students/:studentNumber', lazy: () => import('./StudentSupportPage') },
          { path: '/admin/modules', lazy: () => import('./ModulesAdminPage') },
          { path: '/admin/modules/:code', lazy: () => import('./ModuleAdminPage') },
          { path: '/admin/modules/:code/roster', lazy: () => import('./ModuleAdminPage') },
          { path: '/admin/modules/:code/marks', lazy: () => import('./ModuleAdminPage') },
          { path: '/admin/lecturers', lazy: () => import('./LecturersPage') },
          { path: '/admin/accounts', lazy: () => import('./AccountsPage') },
          { path: '/admin/enrolment', lazy: () => import('./EnrolmentWindowsPage') },
          { path: '/admin/results', lazy: () => import('./ResultsAdminPage') },
          { path: '/admin/announcements', lazy: () => import('./AnnouncementsAdminPage') },
          { path: '/admin/audit', lazy: () => import('./AuditLogPage') },
          { path: '/admin/settings', lazy: () => import('./SettingsPage') },
        ],
      },
    ],
  },
]
