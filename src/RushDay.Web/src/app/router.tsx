import { createBrowserRouter, type RouteObject } from 'react-router'

import { RequireRole } from '@/features/auth'
import { AdminPage } from '@/pages/AdminPage'
import { AnnouncementsPage } from '@/pages/AnnouncementsPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { LoginPage } from '@/pages/LoginPage'
import { ModulesPage } from '@/pages/ModulesPage'
import { NotFoundPage } from '@/pages/NotFoundPage'
import { RouteErrorPage } from '@/pages/RouteErrorPage'
import { StaffPage } from '@/pages/StaffPage'

import { AuthLayout } from './AuthLayout'
import { RootLayout } from './RootLayout'

/**
 * Route table. Exported separately from the router so tests can mount it with createMemoryRouter.
 * Pages are eager for now; switch a route to `lazy` when it grows real weight.
 */
export const routes: RouteObject[] = [
  {
    element: <AuthLayout />,
    errorElement: <RouteErrorPage />,
    children: [{ path: '/login', element: <LoginPage /> }],
  },
  {
    path: '/',
    element: <RootLayout />,
    errorElement: <RouteErrorPage />,
    children: [
      {
        // Pathless boundary: a page that throws keeps the shell around its error.
        errorElement: <RouteErrorPage />,
        children: [
          { index: true, element: <DashboardPage /> },
          { path: 'modules', element: <ModulesPage /> },
          { path: 'announcements', element: <AnnouncementsPage /> },
          {
            path: 'staff',
            element: (
              <RequireRole roles={['Staff', 'Admin']}>
                <StaffPage />
              </RequireRole>
            ),
          },
          {
            path: 'admin',
            element: (
              <RequireRole roles={['Admin']}>
                <AdminPage />
              </RequireRole>
            ),
          },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
]

export const router = createBrowserRouter(routes)
