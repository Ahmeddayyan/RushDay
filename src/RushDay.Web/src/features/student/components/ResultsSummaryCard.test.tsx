import { act, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { queryKeys } from '@/api/keys'
import { makeDashboard, makePublication } from '@/test/handlers/student'
import { renderWithProviders } from '@/test/render'

import { ResultsSummaryCard } from './ResultsSummaryCard'

const NOW = Date.parse('2026-09-28T08:59:50.000Z')

/**
 * Moves the clock in steps of at most 500 ms, each inside its own act(), so React renders and runs
 * effects between timer callbacks as it would in a browser (one long jump inside a single act()
 * would defer every effect, and the timers they schedule, to its end).
 */
async function advance(ms: number) {
  let left = ms
  do {
    const step = Math.min(left, 500)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(step)
    })
    left -= step
  } while (left > 0)
}

describe('ResultsSummaryCard', () => {
  beforeEach(() => {
    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'],
    })
    vi.setSystemTime(NOW)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('countdown reaching zero triggers one invalidation within 30 s', async () => {
    vi.spyOn(Math, 'random').mockReturnValue(0.5)
    const dashboard = makeDashboard({
      nextPublication: makePublication({
        academicYear: '2026/27',
        publishAt: '2026-09-28T09:00:00.000Z',
        state: 'scheduled',
      }),
    })
    const { queryClient } = renderWithProviders(<ResultsSummaryCard dashboard={dashboard} />)
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    await advance(0)

    expect(
      screen.getByText('Autumn 2026/27 results publish 28 September 2026 at 10:00 (BST)'),
    ).toBeInTheDocument()
    expect(
      screen.getByText('Autumn 2026/27 results publish at 28 September 2026 at 10:00 (BST).'),
    ).toHaveClass('sr-only')

    // 09:00:00: the instant passes; nothing is fetched yet, the card says what is happening.
    await advance(10_000)
    expect(screen.getByText('Results are being released…')).toBeInTheDocument()
    expect(invalidate).not.toHaveBeenCalled()

    // The random spread (0.5 × 30 s): still nothing well before it ends ...
    await advance(13_000)
    expect(invalidate).not.toHaveBeenCalled()

    // ... then exactly one invalidation of the dashboard, the results and public status.
    await advance(2_500)
    expect(invalidate).toHaveBeenCalledTimes(1)
    const filters = invalidate.mock.calls[0]?.[0]
    const predicate = filters?.predicate
    expect(predicate).toBeTypeOf('function')
    const matches = (queryKey: readonly unknown[]) =>
      predicate?.({ queryKey } as unknown as Parameters<NonNullable<typeof predicate>>[0])
    expect(matches(queryKeys.student.dashboard)).toBe(true)
    expect(matches(queryKeys.student.results)).toBe(true)
    expect(matches(queryKeys.publicStatus)).toBe(true)
    expect(matches(queryKeys.student.enrolments)).toBe(false)

    await advance(500)
    expect(screen.getByText('Your results are in')).toBeInTheDocument()
    expect(screen.getByText('Your results are in.')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Your results' })).toHaveFocus()

    await advance(5 * 60_000)
    expect(invalidate).toHaveBeenCalledTimes(1)
  })

  it('never waits longer than 30 s', async () => {
    vi.spyOn(Math, 'random').mockReturnValue(0.999)
    const dashboard = makeDashboard({
      nextPublication: makePublication({
        publishAt: '2026-09-28T09:00:00.000Z',
        state: 'scheduled',
      }),
    })
    const { queryClient } = renderWithProviders(<ResultsSummaryCard dashboard={dashboard} />)
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    await advance(10_000 + 31_000)
    expect(invalidate).toHaveBeenCalledTimes(1)
  })

  it('shows the average, the band and the latest publication', async () => {
    renderWithProviders(<ResultsSummaryCard dashboard={makeDashboard()} />)
    await advance(0)
    expect(screen.getByText('Average so far')).toBeInTheDocument()
    expect(screen.getByText('68.0')).toBeInTheDocument()
    expect(screen.getByText('Indicative band: Upper Second (2:1)')).toBeInTheDocument()
    expect(screen.getByText('1 module graded')).toBeInTheDocument()
    expect(
      screen.getByText('Autumn 2025/26 results published 28 September 2026 at 10:00 (BST)'),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'All results' })).toHaveAttribute(
      'href',
      '/student/results',
    )
  })

  it('says so when nothing is graded and nothing is scheduled', async () => {
    renderWithProviders(
      <ResultsSummaryCard
        dashboard={makeDashboard({
          results: [],
          weightedAverage: null,
          classification: null,
          nextPublication: null,
          latestPublication: null,
        })}
      />,
    )
    await advance(0)
    expect(
      screen.getByText("No results yet. Your lecturers haven't published anything."),
    ).toBeInTheDocument()
  })
})
