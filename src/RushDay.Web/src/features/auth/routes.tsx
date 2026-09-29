import type { RouteObject } from 'react-router'

import { ErrorBoundary } from '@/app/ErrorBoundary'
import { PublicOnly, RequireAuth } from '@/app/guards'
import { AppShell } from '@/components/layout/AppShell'

/**
 * Auth area routes, imported statically by app/router.tsx (05-frontend.md section 3). Guards and the
 * shell are pathless layout routes; every page is lazy (`lazy: () => import('./XPage')`, the module
 * exports `Component`). The sign-in page has its own full-page layout; the account pages sit in the
 * signed-in shell, which hides navigation while a gate (`?required=1`) is in force.
 */
export const authRoutes: RouteObject[] = [
  {
    element: <PublicOnly />,
    errorElement: <ErrorBoundary />,
    children: [{ path: '/login', lazy: () => import('./LoginPage') }],
  },
  {
    element: <RequireAuth />,
    errorElement: <ErrorBoundary />,
    children: [
      {
        element: <AppShell />,
        children: [
          { path: '/account', lazy: () => import('./AccountPage') },
          { path: '/account/password', lazy: () => import('./ChangePasswordPage') },
          { path: '/account/mfa', lazy: () => import('./MfaSetupPage') },
        ],
      },
    ],
  },
]
