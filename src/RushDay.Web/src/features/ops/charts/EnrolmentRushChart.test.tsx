import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { loadResultsHandler, makeLoadResults, makeLoadRun } from '@/test/handlers/ops'
import { renderWithProviders } from '@/test/render'

import { EnrolmentRushChart } from './EnrolmentRushChart'

describe('EnrolmentRushChart', () => {
  it('shows the hero figure and a table twin that matches the v0 run, with "After (v1)" not yet measured', async () => {
    const { events } = renderWithProviders(<EnrolmentRushChart />, {
      handlers: [loadResultsHandler(makeLoadResults())],
    })

    expect(await screen.findByText('The enrolment rush')).toBeInTheDocument()
    expect(
      await screen.findByText(/Places given out beyond capacity: 124 → not yet measured/),
    ).toBeInTheDocument()

    await events.click(await screen.findByRole('tab', { name: 'Table' }))
    const table = screen.getByRole('table', { name: 'The enrolment rush: before and after' })
    const beforeRow = within(table).getByText('Before (v0)').closest('tr')
    expect(beforeRow).not.toBeNull()
    // Percentages are of the sum of the four outcome counts (500 here), not the raw k6 `requests`
    // metric (501, one extra teardown call).
    expect(within(beforeRow as HTMLElement).getByText('154 (30.8%)')).toBeInTheDocument()
    expect(within(beforeRow as HTMLElement).getByText('84 (16.8%)')).toBeInTheDocument()
    expect(within(beforeRow as HTMLElement).getByText('262 (52.4%)')).toBeInTheDocument()
    expect(within(table).queryByText('After (v1)')).not.toBeInTheDocument()
  })

  it('renders the after row and drops the "not yet measured" wording once a v1 run exists', async () => {
    const after = makeLoadRun({
      id: 'enrolment-rush-v1',
      version: 'v1',
      label: 'Enrolment rush (v1, atomic update)',
      metrics: {
        requests: 500,
        failedRate: 0,
        p50Ms: 3,
        p95Ms: 12,
        maxMs: 40,
        accepted: 30,
        rejectedFull: 470,
        errored: 0,
        capacity: 30,
        oversold: 0,
      },
    })
    renderWithProviders(<EnrolmentRushChart />, {
      handlers: [loadResultsHandler(makeLoadResults({ runs: [makeLoadRun(), after] }))],
    })

    expect(await screen.findByText('Places given out beyond capacity: 124 → 0')).toBeInTheDocument()
  })

  it('shows an empty state when no enrolment-rush run has been recorded', async () => {
    renderWithProviders(<EnrolmentRushChart />, {
      handlers: [loadResultsHandler(makeLoadResults({ runs: [] }))],
    })
    expect(await screen.findByText('Not yet measured')).toBeInTheDocument()
  })
})
