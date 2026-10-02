import type { RouteObject } from 'react-router'

import { ErrorBoundary } from '@/app/ErrorBoundary'
import { BootSplash, RequireRole } from '@/app/guards'
import { AppShell } from '@/components/layout/AppShell'

/**
 * Lecturer area routes, imported statically by app/router.tsx (05-frontend.md sections 3 and 7).
 * `/lecturer/modules/:code` is a layout route (`ModulePage`) with three lazy tab pages nested under
 * it, each its own URL so a tab can be bookmarked and opened directly.
 */
export const lecturerRoutes: RouteObject[] = [
  {
    element: <RequireRole roles={['Lecturer']} />,
    errorElement: <ErrorBoundary />,
    hydrateFallbackElement: <BootSplash />,
    children: [
      {
        element: <AppShell />,
        children: [
          { path: '/lecturer', lazy: () => import('./LecturerHomePage') },
          { path: '/lecturer/modules', lazy: () => import('./MyModulesPage') },
          {
            path: '/lecturer/modules/:code',
            lazy: () => import('./ModulePage'),
            children: [
              { index: true, lazy: () => import('./RosterTab') },
              { path: 'marks', lazy: () => import('./MarksTab') },
              { path: 'announcements', lazy: () => import('./ModuleAnnouncementsTab') },
            ],
          },
        ],
      },
    ],
  },
]
