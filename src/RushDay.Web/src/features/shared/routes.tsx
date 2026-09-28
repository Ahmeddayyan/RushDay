import type { RouteObject } from 'react-router'

import { RequireAuth } from '@/app/guards'

export const sharedRoutes: RouteObject[] = [
  { path: '/accessibility', lazy: () => import('./AccessibilityPage') },
  { path: '/forbidden', lazy: () => import('./ForbiddenPage') },
  {
    element: <RequireAuth />,
    children: [{ path: '/announcements', lazy: () => import('./AnnouncementsPage') }],
  },
  { path: '*', lazy: () => import('./NotFoundPage') },
]
