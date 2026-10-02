import type { RouteObject } from 'react-router'

import { ErrorBoundary } from '@/app/ErrorBoundary'
import { BootSplash } from '@/app/guards'
import { AppShell } from '@/components/layout/AppShell'

/**
 * `/story` is public (05-frontend.md section 7 and 10): no guard, so a recruiter or a prospective
 * customer can read it without an account. It still renders inside `AppShell`, which shows the
 * wordmark, the theme toggle and "Sign in" for a visitor and the person's own navigation for a
 * signed-in one (05-frontend.md section 3, the same pattern `shared/routes.tsx` uses for
 * `/accessibility` and the catch-all).
 */
export const storyRoutes: RouteObject[] = [
  {
    element: <AppShell />,
    errorElement: <ErrorBoundary />,
    hydrateFallbackElement: <BootSplash />,
    children: [{ path: '/story', lazy: () => import('./StoryPage') }],
  },
]
