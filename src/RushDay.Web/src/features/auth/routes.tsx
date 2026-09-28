import type { RouteObject } from 'react-router'

import { PublicOnly, RequireAuth } from '@/app/guards'

/**
 * Auth area routes, imported statically by app/router.tsx (05-frontend.md section 3). Guard
 * components are pathless layout routes (they render <Outlet /> when allowed), so they carry
 * `children` and are not themselves lazy; every leaf page route is lazy.
 */
export const authRoutes: RouteObject[] = [
  {
    element: <PublicOnly />,
    children: [{ path: '/login', lazy: () => import('./LoginPage') }],
  },
  {
    element: <RequireAuth />,
    children: [
      { path: '/account', lazy: () => import('./AccountPage') },
      { path: '/account/password', lazy: () => import('./ChangePasswordPage') },
      { path: '/account/mfa', lazy: () => import('./MfaSetupPage') },
    ],
  },
]
