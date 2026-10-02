import { act, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { queryKeys } from '@/api/keys'
import { makeAdminMe, makeOpsSnapshot } from '@/test/factories'
import {
  loadResultsHandler,
  makeLoadResults,
  opsMetricsFailureHandler,
  opsMetricsHandler,
} from '@/test/handlers/ops'
import { renderRoutes } from '@/test/render'
import { server } from '@/test/server'

import { opsRoutes } from './routes'

// This route's lazy chunk pulls in all four load-story charts (Recharts is heavier to evaluate
// than a plain page module), which is slower under a full, loaded test run: a generous find
// timeout, and a matching per-test timeout, keep these tests from flaking on a busy machine.
const FIND_OPTIONS = { timeout: 8000 }
const TEST_TIMEOUT = 15_000

describe('OpsPage (/admin/ops)', () => {
  it(
    'keeps showing the last snapshot, greyed, when a poll fails',
    async () => {
      const snapshot = makeOpsSnapshot()

      const { queryClient } = renderRoutes(opsRoutes, {
        route: '/admin/ops',
        user: makeAdminMe(),
        handlers: [opsMetricsHandler(snapshot), loadResultsHandler(makeLoadResults())],
      })

      expect(await screen.findByText('Running normally', {}, FIND_OPTIONS)).toBeInTheDocument()

      // Simulate the next 5 s poll failing, without waiting on the real refetchInterval timer.
      server.use(opsMetricsFailureHandler())
      await act(() => queryClient.refetchQueries({ queryKey: queryKeys.admin.opsMetrics }))

      expect(
        await screen.findByText(
          /Last sample \d+s ago; the server is too busy to answer right now\./,
          {},
          FIND_OPTIONS,
        ),
      ).toBeInTheDocument()
      // The last good sample is still on screen, not an ErrorState.
      expect(screen.getByText('Running normally')).toBeInTheDocument()
      expect(screen.getByRole('status')).toHaveClass('opacity-60')
    },
    TEST_TIMEOUT,
  )

  it(
    'shows the empty-series copy before the server has a minute of history',
    async () => {
      const empty = makeOpsSnapshot()
      const snapshot = {
        ...empty,
        series: empty.series.map((point) => ({ ...point, requests: 0 })),
      }

      renderRoutes(opsRoutes, {
        route: '/admin/ops',
        user: makeAdminMe(),
        handlers: [opsMetricsHandler(snapshot), loadResultsHandler(makeLoadResults())],
      })

      expect(
        await screen.findAllByText('Collecting the first minute of data…', {}, FIND_OPTIONS),
      ).toHaveLength(2)
    },
    TEST_TIMEOUT,
  )

  it(
    "shows the pool meter and the administrator's health summary sentence",
    async () => {
      renderRoutes(opsRoutes, {
        route: '/admin/ops',
        user: makeAdminMe(),
        handlers: [opsMetricsHandler(makeOpsSnapshot()), loadResultsHandler(makeLoadResults())],
      })

      expect(
        await screen.findByText('Database connections in use', {}, FIND_OPTIONS),
      ).toBeInTheDocument()
      expect(screen.getByText(/2 of 20 connections in use/)).toBeInTheDocument()
      expect(screen.getByText(/Last minute: 1,200 requests/)).toBeInTheDocument()
    },
    TEST_TIMEOUT,
  )
})
