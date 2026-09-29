import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { loadResultsHandler, makeLoadResults, makeLoadRun } from '@/test/handlers/ops'
import { renderWithProviders } from '@/test/render'

import { ResultsDayTiles } from './ResultsDayTiles'

function resultsDayRun() {
  return makeLoadRun({
    id: 'results-day-20260927-201336',
    scenario: 'results-day',
    version: 'v0',
    label: 'Results day (v0, N+1 dashboard query)',
    targetRate: 800,
    metrics: {
      requests: 104_399,
      failedRate: 0,
      p50Ms: 3.665,
      p95Ms: 6.52,
      maxMs: 358.5,
      achievedRate: 435,
    },
  })
}

describe('ResultsDayTiles', () => {
  it('shows the v0 headline numbers, "not yet measured" for After, and the fixed query-count fact', async () => {
    const { events } = renderWithProviders(<ResultsDayTiles />, {
      handlers: [loadResultsHandler(makeLoadResults({ runs: [resultsDayRun()] }))],
    })

    expect(await screen.findByText('Results day')).toBeInTheDocument()
    // The title renders in both the loaded and "not yet measured" states, so wait for a
    // data-dependent value before asserting on the rest of the loaded content.
    expect(await screen.findByText('800/s')).toBeInTheDocument()
    expect(screen.getByText('6.5 ms')).toBeInTheDocument()

    await events.click(screen.getByRole('tab', { name: 'Table' }))
    const table = screen.getByRole('table', { name: 'Results day: before and after' })
    const queriesRow = within(table)
      .getByText('Database round trips per page')
      .closest('tr') as HTMLElement
    expect(within(queriesRow).getByText('15')).toBeInTheDocument()
    expect(within(queriesRow).getByText('5')).toBeInTheDocument()

    const peakRow = within(table).getByText('Peak load reached').closest('tr') as HTMLElement
    expect(within(peakRow).getByText('not yet measured')).toBeInTheDocument()
  })

  it('shows an empty state when no results-day run has been recorded', async () => {
    renderWithProviders(<ResultsDayTiles />, {
      handlers: [loadResultsHandler(makeLoadResults({ runs: [] }))],
    })
    expect(await screen.findByText('Not yet measured')).toBeInTheDocument()
  })
})
