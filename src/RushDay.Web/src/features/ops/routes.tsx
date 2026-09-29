import type { RouteObject } from 'react-router'

import { ErrorBoundary } from '@/app/ErrorBoundary'
import { BootSplash, RequireRole } from '@/app/guards'
import { AppShell } from '@/components/layout/AppShell'

/**
 * `/admin/ops` (05-frontend.md sections 7 and 10): administrators only, 1400 px content width
 * (`AppShell wide`) for the tiles and charts.
 */
export const opsRoutes: RouteObject[] = [
  {
    element: <RequireRole roles={['Admin']} />,
    errorElement: <ErrorBoundary />,
    hydrateFallbackElement: <BootSplash />,
    children: [
      {
        element: <AppShell wide />,
        children: [{ path: '/admin/ops', lazy: () => import('./OpsPage') }],
      },
    ],
  },
]
