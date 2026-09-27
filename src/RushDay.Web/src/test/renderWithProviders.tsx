import type { ReactElement } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, type RenderResult } from '@testing-library/react'
import { RouterProvider, createMemoryRouter, type RouteObject } from 'react-router'

import { routes as appRoutes } from '@/app/router'
import { AuthProvider } from '@/features/auth'
import { ThemeProvider } from '@/features/theme'

export interface RenderOptions {
  /** Initial URL for the memory router. */
  route?: string
  /** Route table to mount; defaults to the real application routes. */
  routes?: RouteObject[]
}

export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: Infinity },
      mutations: { retry: false },
    },
  })
}

/** Mounts the given routes inside the same provider stack as main.tsx, on an in-memory history. */
export function renderApp({ route = '/', routes = appRoutes }: RenderOptions = {}) {
  const router = createMemoryRouter(routes, { initialEntries: [route] })
  const queryClient = createTestQueryClient()
  const result = render(
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <AuthProvider>
          <RouterProvider router={router} />
        </AuthProvider>
      </ThemeProvider>
    </QueryClientProvider>,
  )
  return { ...result, router, queryClient }
}

/** Mounts a single element with the provider stack but no routing. */
export function renderWithProviders(ui: ReactElement): RenderResult & { queryClient: QueryClient } {
  const queryClient = createTestQueryClient()
  const result = render(
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <AuthProvider>{ui}</AuthProvider>
      </ThemeProvider>
    </QueryClientProvider>,
  )
  return { ...result, queryClient }
}
