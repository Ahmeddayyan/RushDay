import { createBrowserRouter, type RouteObject } from 'react-router'

import { adminRoutes } from '@/features/admin/routes'
import { authRoutes } from '@/features/auth/routes'
import { lecturerRoutes } from '@/features/lecturer/routes'
import { opsRoutes } from '@/features/ops/routes'
import { sharedRoutes } from '@/features/shared/routes'
import { storyRoutes } from '@/features/story/routes'
import { studentRoutes } from '@/features/student/routes'

import { ErrorBoundary } from './ErrorBoundary'
import { RootRedirect } from './guards'

/**
 * The route table (05-frontend.md section 3 and 7). Written once, in full, in stage S3: the seven
 * area route tables below are imported statically because paths are known up front. Stages S7-S10
 * only ever fill in their own routes.tsx and never edit this file.
 */
export const routes: RouteObject[] = [
  { path: '/', element: <RootRedirect />, errorElement: <ErrorBoundary /> },
  ...authRoutes,
  ...sharedRoutes,
  ...studentRoutes,
  ...lecturerRoutes,
  ...adminRoutes,
  ...opsRoutes,
  ...storyRoutes,
]

export const router = createBrowserRouter(routes)
