import type { RouteObject } from 'react-router'

import { ErrorBoundary } from '@/app/ErrorBoundary'
import { RequireAuth } from '@/app/guards'
import { AppShell } from '@/components/layout/AppShell'

/**
 * Pages every visitor can reach. The public ones (/accessibility, /forbidden, the catch-all) use the
 * shell too: signed in, it brings the person's navigation; signed out, a wordmark and Sign in.
 */
export const sharedRoutes: RouteObject[] = [
  {
    element: <AppShell />,
    errorElement: <ErrorBoundary />,
    children: [
      { path: '/accessibility', lazy: () => import('./AccessibilityPage') },
      { path: '/forbidden', lazy: () => import('./ForbiddenPage') },
      { path: '*', lazy: () => import('./NotFoundPage') },
    ],
  },
  {
    element: <RequireAuth />,
    errorElement: <ErrorBoundary />,
    children: [
      {
        element: <AppShell />,
        children: [{ path: '/announcements', lazy: () => import('./AnnouncementsPage') }],
      },
    ],
  },
]
