import type { ReactElement } from 'react'
import { QueryClient } from '@tanstack/react-query'
import { render, type RenderResult } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { RequestHandler } from 'msw'
import {
  createMemoryRouter,
  MemoryRouter,
  Route,
  RouterProvider,
  Routes,
  type RouteObject,
} from 'react-router'

import type { Me } from '@/api/types/common'

import { makeStudentMe } from './factories'
import { setSessionUser } from './handlers/auth'
import { server } from './server'
import { TestFrame } from './TestFrame'

/** A QueryClient for one test: no retries, no refetch on focus. */
export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: Infinity, staleTime: 0, refetchOnWindowFocus: false },
      mutations: { retry: 0 },
    },
  })
}

export interface RenderOptions {
  /** The URL the MemoryRouter starts at. */
  route?: string
  /** A route pattern to mount `ui` under, so `useParams` works (`/student/modules/:code`). */
  path?: string
  /** The signed-in session (defaults to a student); null renders signed out. */
  user?: Me | null
  /** Extra MSW handlers for this test; reset after it by src/test/setup.ts. */
  handlers?: RequestHandler[]
  queryClient?: QueryClient
}

export interface RenderWithProvidersResult extends RenderResult {
  queryClient: QueryClient
  /** `userEvent.setup()`, ready to use. */
  events: ReturnType<typeof userEvent.setup>
}

/**
 * Renders `ui` inside a fresh QueryClient (retries off), a MemoryRouter and an AuthProvider primed
 * with a `Me` fixture (05-frontend.md section 13.1). The MSW auth handlers answer for the same session.
 */
export function renderWithProviders(
  ui: ReactElement,
  options: RenderOptions = {},
): RenderWithProvidersResult {
  const { route = '/', path, handlers = [], queryClient = createTestQueryClient() } = options
  const user = options.user === undefined ? makeStudentMe() : options.user
  if (handlers.length > 0) server.use(...handlers)
  setSessionUser(user)

  const result = render(
    <TestFrame client={queryClient} user={user}>
      <MemoryRouter initialEntries={[route]}>
        {path ? (
          <Routes>
            <Route path={path} element={ui} />
          </Routes>
        ) : (
          ui
        )}
      </MemoryRouter>
    </TestFrame>,
  )
  return { ...result, queryClient, events: userEvent.setup() }
}

export interface RenderRoutesResult extends RenderWithProvidersResult {
  router: ReturnType<typeof createMemoryRouter>
}

/**
 * Renders a route table in a memory data router (lazy routes, guards, redirects). `boot: true`
 * starts from `booting` and goes through `GET /api/auth/me` like the real app.
 */
export function renderRoutes(
  routes: RouteObject[],
  options: Omit<RenderOptions, 'path'> & { boot?: boolean } = {},
): RenderRoutesResult {
  const {
    route = '/',
    handlers = [],
    queryClient = createTestQueryClient(),
    boot = false,
  } = options
  const user = options.user === undefined ? makeStudentMe() : options.user
  if (handlers.length > 0) server.use(...handlers)
  setSessionUser(user)

  const router = createMemoryRouter(routes, { initialEntries: [route] })
  const result = render(
    <TestFrame client={queryClient} {...(boot ? {} : { user })}>
      <RouterProvider router={router} />
    </TestFrame>,
  )
  return { ...result, queryClient, router, events: userEvent.setup() }
}
