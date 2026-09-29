import { configure, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { downloadText } from '@/lib/download'
import { makeModuleDetail, moduleHandler } from '@/test/handlers/modules'
import {
  dashboardHandler,
  enrolmentsHandler,
  makeCompleted,
  makeDashboard,
  makeMyEnrolment,
  makeTimetableSlot,
  timetableHandler,
} from '@/test/handlers/student'
import { makeLecturerMe } from '@/test/factories'
import { renderRoutes } from '@/test/render'

import { studentRoutes } from './routes'

// Pages load through lazy routes and these tests click through whole journeys; under a parallel
// coverage run a first render can take over a second, so both budgets are raised for this file.
configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 20_000 })

vi.mock('@/lib/download', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/download')>()),
  downloadText: vi.fn(),
}))

describe('StudentDashboardPage', () => {
  it('greets the student and shows every card from one request', async () => {
    renderRoutes(studentRoutes, {
      route: '/student',
      handlers: [
        dashboardHandler(
          makeDashboard({
            completed: [
              makeCompleted(),
              makeCompleted({
                code: 'CS2002',
                title: 'Databases',
                outcome: 'absent',
                mark: null,
                band: null,
              }),
              makeCompleted({
                code: 'CS1001',
                academicYear: '2024/25',
                outcome: null,
                mark: null,
                band: null,
              }),
            ],
          }),
        ),
      ],
    })

    expect(
      await screen.findByRole('heading', {
        level: 1,
        name: /^Good (morning|afternoon|evening), Aisha$/,
      }),
    ).toBeInTheDocument()
    expect(screen.getByText('S000001')).toBeInTheDocument()
    expect(screen.getByText('BSc Computer Science')).toBeInTheDocument()
    expect(screen.getByText('Year 2')).toBeInTheDocument()

    expect(screen.getByText('68.0')).toBeInTheDocument()
    expect(screen.getByText('Indicative band: Upper Second (2:1)')).toBeInTheDocument()

    expect(screen.getByRole('meter', { name: 'Autumn credits' })).toHaveAttribute(
      'aria-valuetext',
      '15 of 60 credits, Autumn 2026/27',
    )
    expect(screen.getByRole('link', { name: 'Browse modules' })).toHaveAttribute(
      'href',
      '/student/modules',
    )

    expect(screen.getByRole('heading', { name: 'Next classes' })).toBeInTheDocument()
    expect(screen.getByText('Autumn timetable')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Full timetable/ })).toHaveAttribute(
      'href',
      '/student/timetable',
    )

    expect(
      screen.getByRole('heading', { name: 'Autumn 2025/26 results are available' }),
    ).toBeInTheDocument()

    const current = screen
      .getByRole('heading', { name: "This year's modules" })
      .closest('div.rounded-lg')
    expect(
      within(current as HTMLElement).getByRole('link', { name: 'CS3001 Distributed Systems' }),
    ).toHaveAttribute('href', '/student/modules/CS3001')

    const completed = screen.getByRole('region', { name: '2025/26' })
    expect(within(completed).getByText('Mark 68 (2:1)')).toBeInTheDocument()
    expect(within(completed).getByText('Absent')).toBeInTheDocument()
    expect(
      within(screen.getByRole('region', { name: '2024/25' })).getByText('Result not yet published'),
    ).toBeInTheDocument()
  })

  it('invites a student with no modules to browse', async () => {
    renderRoutes(studentRoutes, {
      route: '/student',
      handlers: [
        dashboardHandler(
          makeDashboard({
            modules: [],
            completed: [],
            timetable: [],
            results: [],
            weightedAverage: null,
            classification: null,
            latestPublication: null,
            announcements: [],
          }),
        ),
      ],
    })
    expect(await screen.findByText("You're not enrolled on any modules yet.")).toBeInTheDocument()
    expect(
      screen.getByText("No results yet. Your lecturers haven't published anything."),
    ).toBeInTheDocument()
    expect(screen.getByText('No classes this semester.')).toBeInTheDocument()
    expect(screen.getByText('No completed modules yet.')).toBeInTheDocument()
  })

  it('sends other roles to /forbidden', async () => {
    const { router } = renderRoutes(
      [...studentRoutes, { path: '/forbidden', element: <p>Forbidden</p> }],
      { route: '/student', user: makeLecturerMe() },
    )
    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})

describe('TimetablePage', () => {
  it('shows the current semester and downloads the week as an .ics file', async () => {
    const { events } = renderRoutes(studentRoutes, {
      route: '/student/timetable',
      handlers: [
        timetableHandler([
          makeTimetableSlot(),
          makeTimetableSlot({
            moduleCode: 'CS3099',
            moduleTitle: 'Software Engineering Project',
            day: 'thursday',
          }),
        ]),
      ],
    })

    expect(await screen.findByRole('table', { name: 'Timetable' })).toBeInTheDocument()
    expect(screen.getAllByText('Autumn 2026/27 timetable').length).toBeGreaterThan(0)
    expect(screen.getByRole('tablist', { name: 'Day' })).toBeInTheDocument()

    await events.click(
      screen.getByRole('button', { name: 'Add to calendar (.ics), downloads a file' }),
    )
    expect(downloadText).toHaveBeenCalledTimes(1)
    const [text, filename, type] = vi.mocked(downloadText).mock.calls[0] ?? []
    expect(text?.startsWith('BEGIN:VCALENDAR')).toBe(true)
    expect(text).toContain('SUMMARY:CS3099 Software Engineering Project (Lecture)')
    expect(filename).toBe('rushday-timetable-autumn-2026-27.ics')
    expect(type).toBe('text/calendar;charset=utf-8')
  })

  it('points to the catalogue when there are no classes', async () => {
    renderRoutes(studentRoutes, { route: '/student/timetable', handlers: [timetableHandler([])] })
    expect(await screen.findByText('No classes this semester.')).toBeInTheDocument()
    expect(screen.getByText('Enrol on modules to build your timetable.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Add to calendar/ })).not.toBeInTheDocument()
  })
})

describe('ModuleDetailPage', () => {
  it('shows the module, live places, timetable and "Not enrolled" with Enrol', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/modules/CS3099',
      handlers: [
        moduleHandler([makeModuleDetail()]),
        enrolmentsHandler([]),
        dashboardHandler(makeDashboard()),
      ],
    })

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Software Engineering Project' }),
    ).toBeInTheDocument()
    expect(screen.getByText('15 credits')).toBeInTheDocument()
    expect(screen.getByText('Level 3')).toBeInTheDocument()
    expect(screen.getByText('Dr Grace Hopper (leader)')).toBeInTheDocument()
    expect(screen.getByRole('meter', { name: 'Places filled on CS3099' })).toHaveAttribute(
      'aria-valuetext',
      '12 of 30 places left',
    )
    expect(screen.getByText(/Work in a team of five/)).toBeInTheDocument()
    expect(screen.getByText('Lecture · C-104')).toBeInTheDocument()
    expect(screen.getByText('Lab · C-Lab2')).toBeInTheDocument()
    expect(await screen.findByText('Not enrolled')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Enrol on CS3099' })).toBeEnabled()
  })

  it('says since when the student is enrolled', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/modules/CS3099',
      handlers: [
        moduleHandler([makeModuleDetail()]),
        enrolmentsHandler([makeMyEnrolment({ moduleCode: 'CS3099' })]),
        dashboardHandler(makeDashboard()),
      ],
    })
    expect(await screen.findByText('Enrolled since 14 September 2026')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Withdraw from CS3099' })).toBeInTheDocument()
  })

  it('offers re-enrolment after a withdrawal while the window is open', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/modules/CS3099',
      handlers: [
        moduleHandler([makeModuleDetail()]),
        enrolmentsHandler([
          makeMyEnrolment({
            moduleCode: 'CS3099',
            status: 'withdrawn',
            withdrawnAt: '2026-09-20T10:00:00.000Z',
            canWithdraw: false,
          }),
        ]),
        dashboardHandler(makeDashboard()),
      ],
    })
    expect(await screen.findByText('You withdrew on 20 September 2026.')).toBeInTheDocument()
    expect(screen.getByText('You can re-enrol while places remain.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Enrol again on CS3099' })).toBeEnabled()
  })

  it('shows a completed module with its mark', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/modules/CS2001',
      handlers: [
        moduleHandler([
          makeModuleDetail({ code: 'CS2001', title: 'Algorithms and Data Structures' }),
        ]),
        enrolmentsHandler([
          makeMyEnrolment({
            moduleCode: 'CS2001',
            academicYear: '2025/26',
            canWithdraw: false,
            withdrawBlockedReason: 'year',
          }),
        ]),
        dashboardHandler(makeDashboard()),
      ],
    })
    expect(await screen.findByText('Completed 2025/26: mark 68 (2:1)')).toBeInTheDocument()
  })

  it('shows an inactive module with a banner and a disabled button', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/modules/CS3099',
      handlers: [
        moduleHandler([makeModuleDetail({ isActive: false })]),
        enrolmentsHandler([]),
        dashboardHandler(makeDashboard()),
      ],
    })
    expect(await screen.findByText('This module is no longer running')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Not running' })).toBeDisabled()
  })

  it('answers an unknown module with an empty state', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/modules/ZZ9999',
      handlers: [moduleHandler([]), enrolmentsHandler([]), dashboardHandler(makeDashboard())],
    })
    expect(await screen.findByText("That page or record doesn't exist.")).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Browse modules' })).toHaveAttribute(
      'href',
      '/student/modules',
    )
  })

  it('never requests a code that cannot exist', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/modules/not-a-code',
      handlers: [enrolmentsHandler([]), dashboardHandler(makeDashboard())],
    })
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Module not found' }),
    ).toBeInTheDocument()
  })
})
