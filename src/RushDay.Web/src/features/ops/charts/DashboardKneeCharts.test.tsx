import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import type { LoadRun } from '@/api/types/loadResults'
import { loadResultsHandler, makeLoadResults } from '@/test/handlers/ops'
import { renderWithProviders } from '@/test/render'

import { DashboardKneeCharts } from './DashboardKneeCharts'

function kneeRun(
  targetRate: number,
  p95Ms: number,
  achievedRate: number,
  droppedIterations: number,
): LoadRun {
  return {
    id: `dashboard-knee-${targetRate}rps`,
    scenario: 'dashboard-knee',
    version: 'v0',
    label: `Dashboard knee at ${targetRate} requests/s (v0)`,
    ranAt: '2026-09-27T20:18:44',
    source: `load/results/dashboard-knee-${targetRate}rps-20260927-201844.json`,
    targetRate,
    metrics: {
      requests: 1000,
      failedRate: 0,
      p50Ms: 5,
      p95Ms,
      maxMs: p95Ms * 2,
      achievedRate,
      droppedIterations,
    },
  }
}

describe('DashboardKneeCharts', () => {
  it('renders a table twin with one row per target load and "not yet measured" for After (v1)', async () => {
    const runs = [kneeRun(1000, 2486, 870, 2935), kneeRun(2000, 7327, 697, 36_723)]
    const { events } = renderWithProviders(<DashboardKneeCharts />, {
      handlers: [loadResultsHandler(makeLoadResults({ runs }))],
    })

    expect(await screen.findByText('Where the dashboard breaks')).toBeInTheDocument()
    // The title renders in both the loaded and "not yet measured" states, so wait for the tab
    // (only present once the query has resolved) before asserting on the loaded content.
    await events.click(await screen.findByRole('tab', { name: 'Table' }))

    const table = screen.getByRole('table', {
      name: 'Where the dashboard breaks: before and after, by target load',
    })
    const row1000 = within(table).getByText('1,000/s').closest('tr') as HTMLElement
    expect(within(row1000).getByText('2,486')).toBeInTheDocument()
    expect(within(row1000).getByText('870')).toBeInTheDocument()
    expect(within(row1000).getAllByText('not yet measured').length).toBeGreaterThan(0)
  })

  it('shows an empty state when no dashboard-knee run has been recorded', async () => {
    renderWithProviders(<DashboardKneeCharts />, {
      handlers: [loadResultsHandler(makeLoadResults({ runs: [] }))],
    })
    expect(await screen.findByText('Not yet measured')).toBeInTheDocument()
  })
})
