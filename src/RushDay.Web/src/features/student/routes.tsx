import type { RouteObject } from 'react-router'

import { ErrorBoundary } from '@/app/ErrorBoundary'
import { BootSplash, RequireRole } from '@/app/guards'
import { AppShell } from '@/components/layout/AppShell'

/**
 * Student area routes (05-frontend.md sections 3 and 7), imported statically by app/router.tsx.
 * `RequireRole` sends anyone else to /forbidden; every page is lazy, so a student never downloads
 * lecturer or admin code and the other roles never download this. On a deep link the page chunk
 * loads before anything renders; the boot splash covers that moment instead of a blank page.
 */
export const studentRoutes: RouteObject[] = [
  {
    element: <RequireRole roles={['Student']} />,
    errorElement: <ErrorBoundary />,
    hydrateFallbackElement: <BootSplash />,
    children: [
      {
        element: <AppShell />,
        children: [
          { path: '/student', lazy: () => import('./StudentDashboardPage') },
          { path: '/student/results', lazy: () => import('./ResultsPage') },
          { path: '/student/timetable', lazy: () => import('./TimetablePage') },
          { path: '/student/modules', lazy: () => import('./CataloguePage') },
          { path: '/student/modules/:code', lazy: () => import('./ModuleDetailPage') },
        ],
      },
    ],
  },
]
