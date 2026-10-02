import { configure, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { formatDateTime, formatShortDate } from '@/lib/format'
import {
  makeGrade,
  makeResults,
  makeResultsSemester,
  resultsHandler,
} from '@/test/handlers/student'
import { renderRoutes } from '@/test/render'

import { studentRoutes } from './routes'

// Pages load through lazy routes and these tests click through whole journeys; under a parallel
// coverage run a first render can take over a second, so both budgets are raised for this file.
configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 20_000 })

function rowOf(table: HTMLElement, code: string): HTMLElement {
  const cell = within(table).getByText(code)
  return cell.closest('tr') as HTMLElement
}

describe('ResultsPage', () => {
  it('groups by academic year and semester, newest year first, autumn before spring', async () => {
    const publishAt = new Date(Date.now() + 3 * 24 * 3600_000).toISOString()
    renderRoutes(studentRoutes, {
      route: '/student/results',
      handlers: [
        resultsHandler(
          makeResults({
            semesters: [
              makeResultsSemester({
                semester: 'spring',
                results: [makeGrade({ moduleCode: 'CS2010', semester: 'spring' })],
              }),
              makeResultsSemester({
                academicYear: '2024/25',
                semester: 'spring',
                results: [makeGrade({ moduleCode: 'CS1010', academicYear: '2024/25' })],
              }),
              makeResultsSemester(),
              makeResultsSemester({
                academicYear: '2026/27',
                state: 'scheduled',
                publishAt,
                results: [],
              }),
              makeResultsSemester({
                academicYear: '2026/27',
                semester: 'spring',
                state: 'pending',
                results: [],
              }),
            ],
          }),
        ),
      ],
    })

    await screen.findByRole('table', { name: 'Autumn 2025/26' })
    const headings = screen
      .getAllByRole('heading', { level: 2 })
      .map((heading) => heading.textContent)
    expect(headings).toEqual([
      'Autumn 2026/27',
      'Spring 2026/27',
      'Autumn 2025/26',
      'Spring 2025/26',
      'Spring 2024/25',
    ])
    expect(screen.getAllByRole('table')).toHaveLength(3)

    // Scheduled: when it publishes, with a countdown.
    const scheduled = screen.getByRole('region', { name: 'Autumn 2026/27' })
    expect(
      within(scheduled).getByText(`Publishes ${formatDateTime(publishAt, 'Europe/London')}`),
    ).toBeInTheDocument()
    expect(within(scheduled).getByRole('status')).toHaveTextContent('About 3 days to go')
    // Pending.
    expect(
      within(screen.getByRole('region', { name: 'Spring 2026/27' })).getByText(
        'Results not yet published',
      ),
    ).toBeInTheDocument()
  })

  it('shows marks with their bands, Absent and Deferred in words, and the Amended badge', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/results',
      handlers: [
        resultsHandler(
          makeResults({
            semesters: [
              makeResultsSemester({
                results: [
                  makeGrade({ moduleCode: 'CS2001', mark: 70 }),
                  makeGrade({ moduleCode: 'CS2002', mark: 60 }),
                  makeGrade({ moduleCode: 'CS2003', mark: 50 }),
                  makeGrade({ moduleCode: 'CS2004', mark: 40 }),
                  makeGrade({
                    moduleCode: 'CS2005',
                    mark: 39,
                    correctedAt: '2026-09-30T11:00:00.000Z',
                  }),
                  makeGrade({ moduleCode: 'CS2006', outcome: 'absent', mark: null }),
                  makeGrade({ moduleCode: 'CS2007', outcome: 'deferred', mark: null }),
                ],
              }),
            ],
          }),
        ),
      ],
    })

    const table = await screen.findByRole('table', { name: 'Autumn 2025/26' })
    const band = (code: string) => within(rowOf(table, code)).getAllByRole('cell')[4]
    expect(band('CS2001')).toHaveTextContent('First')
    expect(band('CS2002')).toHaveTextContent('2:1')
    expect(band('CS2003')).toHaveTextContent('2:2')
    expect(band('CS2004')).toHaveTextContent('Third')
    expect(band('CS2005')).toHaveTextContent('Fail')
    expect(band('CS2005')?.querySelector('svg')).not.toBeNull()
    expect(band('CS2005')?.firstElementChild).toHaveClass('text-danger')

    const absent = within(rowOf(table, 'CS2006')).getAllByRole('cell')
    expect(absent[3]).toHaveTextContent('Absent')
    expect(absent[4]).toHaveTextContent('Absent')
    expect(within(rowOf(table, 'CS2007')).getAllByRole('cell')[3]).toHaveTextContent('Deferred')

    expect(
      within(rowOf(table, 'CS2005')).getByText(
        `Amended ${formatShortDate('2026-09-30T11:00:00.000Z')}`,
      ),
    ).toBeInTheDocument()
    expect(within(rowOf(table, 'CS2001')).queryByText(/Amended/)).not.toBeInTheDocument()
    expect(within(rowOf(table, 'CS2001')).getByText('26 Jan 2026')).toHaveAttribute(
      'datetime',
      '2026-01-26T09:00:00.000Z',
    )
  })

  it('shows the credit-weighted average (80×30 + 50×15 → 70.0) with absences excluded', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/results',
      handlers: [
        resultsHandler(
          makeResults({
            semesters: [
              makeResultsSemester({
                results: [
                  makeGrade({ moduleCode: 'CS2001', mark: 80, credits: 30 }),
                  makeGrade({ moduleCode: 'CS2002', mark: 50, credits: 15 }),
                  makeGrade({ moduleCode: 'CS2003', outcome: 'absent', mark: null, credits: 30 }),
                ],
              }),
            ],
            weightedAverage: 70,
            classification: 'First',
          }),
        ),
      ],
    })

    expect(await screen.findByText('70.0')).toBeInTheDocument()
    expect(screen.getByText('Average so far')).toBeInTheDocument()
    expect(screen.getByText('Indicative band: First')).toBeInTheDocument()
    expect(screen.getByText('2 modules graded')).toBeInTheDocument()
    expect(screen.getByText('How this is calculated')).toBeInTheDocument()
    expect(
      screen.getByText(
        'Your degree classification is decided by the exam board on your whole programme.',
      ),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Below 40? Your personal tutor or the academic office can explain resit options.',
      ),
    ).toBeInTheDocument()
  })

  it('computes the average itself when the response has none', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/results',
      handlers: [
        resultsHandler(
          makeResults({
            semesters: [
              makeResultsSemester({
                results: [
                  makeGrade({ moduleCode: 'CS2001', mark: 80, credits: 30 }),
                  makeGrade({ moduleCode: 'CS2002', mark: 50, credits: 15 }),
                  makeGrade({ moduleCode: 'CS2003', outcome: 'deferred', mark: null }),
                ],
              }),
            ],
            weightedAverage: null,
            classification: null,
          }),
        ),
      ],
    })
    expect(await screen.findByText('70.0')).toBeInTheDocument()
  })

  it('prints an unofficial summary', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => {})
    const { events } = renderRoutes(studentRoutes, {
      route: '/student/results',
      handlers: [resultsHandler(makeResults())],
    })
    await events.click(await screen.findByRole('button', { name: 'Print results summary' }))
    expect(print).toHaveBeenCalledTimes(1)
    expect(screen.getByText('Unofficial results summary, not a transcript')).toBeInTheDocument()
  })

  it('says when there are no results yet', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/results',
      handlers: [
        resultsHandler(makeResults({ semesters: [], weightedAverage: null, classification: null })),
      ],
    })
    expect(await screen.findByText('No results yet.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Print results summary' })).not.toBeInTheDocument()
  })
})
