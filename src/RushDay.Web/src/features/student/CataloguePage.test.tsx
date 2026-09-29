import { configure, screen, waitFor, within } from '@testing-library/react'
import { http } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { queryKeys } from '@/api/keys'
import { makePublicStatus } from '@/test/factories'
import { problem } from '@/test/http'
import { catalogueHandler, makeCatalogue, makeModule } from '@/test/handlers/modules'
import { statusHandler } from '@/test/handlers/public'
import {
  createEnrolmentServer,
  enrolmentsHandler,
  makeCompleted,
  makeDashboard,
  makeMyEnrolment,
  makeStudentWindow,
} from '@/test/handlers/student'
import { createTestQueryClient, renderRoutes } from '@/test/render'

import { studentRoutes } from './routes'

// Pages load through lazy routes and these tests click through whole journeys; under a parallel
// coverage run a first render can take over a second, so both budgets are raised for this file.
configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 20_000 })

function card(code: string): HTMLElement {
  const link = screen.getByRole('link', { name: new RegExp(`^${code}`) })
  const item = link.closest('li')
  if (!item) throw new Error(`no card for ${code}`)
  return item
}

describe('CataloguePage filters', () => {
  it('reads the filters from the URL, writes changes back and counts the matches', async () => {
    const { router, events } = renderRoutes(studentRoutes, {
      route: '/student/modules?semester=spring',
      handlers: [catalogueHandler(makeCatalogue()), enrolmentsHandler([])],
    })

    expect(await screen.findByRole('link', { name: /^MA2004/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /^CS3099/ })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Semester')).toHaveValue('spring')
    const count = screen.getByText('2 of 6 modules')
    expect(count).toHaveAttribute('aria-live', 'polite')

    await events.selectOptions(screen.getByLabelText('Department'), 'EE')
    expect(router.state.location.search).toBe('?semester=spring&dept=EE')
    expect(await screen.findByText('1 of 6 modules')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /^EE2010/ })).toBeInTheDocument()

    await events.selectOptions(screen.getByLabelText('Semester'), '')
    await events.selectOptions(screen.getByLabelText('Department'), '')
    await events.type(screen.getByLabelText('Search by code or title'), 'systems')
    // Debounced by 250 ms: the URL and the list follow once typing stops.
    await waitFor(() => expect(router.state.location.search).toBe('?q=systems'))
    expect(await screen.findByText('2 of 6 modules')).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: /^CS3001/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /^EE2010/ })).toBeInTheDocument()

    await events.click(screen.getByLabelText('Enrolled only'))
    expect(router.state.location.search).toBe('?q=systems&mine=1')
    expect(await screen.findByText('No modules match these filters')).toBeInTheDocument()
    expect(screen.getByText('0 of 6 modules')).toBeInTheDocument()

    const clear = screen.getAllByRole('button', { name: 'Clear filters' })
    await events.click(clear[clear.length - 1] as HTMLElement)
    expect(router.state.location.search).toBe('')
    expect(screen.getByLabelText('Search by code or title')).toHaveValue('')
    expect(await screen.findByText('6 modules')).toBeInTheDocument()
  })

  it('filters by level and availability and shows the empty state with Clear filters', async () => {
    const { router, events } = renderRoutes(studentRoutes, {
      route: '/student/modules?level=3&availability=full',
      handlers: [catalogueHandler(makeCatalogue()), enrolmentsHandler([])],
    })
    expect(await screen.findByRole('link', { name: /^CS3001/ })).toBeInTheDocument()
    expect(screen.getByText('1 of 6 modules')).toBeInTheDocument()

    await events.selectOptions(screen.getByLabelText('Level'), '1')
    expect(await screen.findByText('No modules match these filters')).toBeInTheDocument()
    await events.click(
      within(
        screen.getByText('No modules match these filters').parentElement as HTMLElement,
      ).getByRole('button', { name: 'Clear filters' }),
    )
    expect(router.state.location.search).toBe('')
    expect(await screen.findByText('6 modules')).toBeInTheDocument()
  })

  it('shows a banner per semester that is not open, the caption and both credit budgets', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/modules',
      handlers: [
        statusHandler(
          makePublicStatus({
            enrolmentWindows: [
              makeStudentWindow({ state: 'closed' }),
              makeStudentWindow({
                semester: 'spring',
                state: 'notYetOpen',
                opensAt: '2027-01-04T09:00:00.000Z',
              }),
            ],
          }),
        ),
        catalogueHandler(makeCatalogue()),
        enrolmentsHandler([makeMyEnrolment()]),
      ],
    })
    expect(
      await screen.findByText('Enrolment for Autumn closed on 2 October 2026 at 18:00 (BST).'),
    ).toBeInTheDocument()
    expect(
      screen.getByText('Enrolment for Spring opens 4 January 2027 at 09:00 (GMT).'),
    ).toBeInTheDocument()
    expect(
      screen.getByText('Places update every 30 seconds. Open a module for live numbers.'),
    ).toBeInTheDocument()
    expect(await screen.findByRole('meter', { name: 'Autumn credits' })).toHaveAttribute(
      'aria-valuetext',
      '15 of 60 credits, Autumn 2026/27',
    )
    expect(screen.getByRole('meter', { name: 'Spring credits' })).toHaveAttribute(
      'aria-valuetext',
      '0 of 60 credits, Spring 2026/27',
    )
  })
})

describe('CataloguePage enrol states', () => {
  it('shows every EnrolButton state of the table', async () => {
    const modules = [
      makeModule({ code: 'CS3001', title: 'Distributed Systems' }),
      makeModule({ code: 'CS3002', title: 'Compilers' }),
      makeModule({ code: 'CS3003', title: 'Security' }),
      makeModule({ code: 'CS2001', title: 'Algorithms and Data Structures' }),
      makeModule({ code: 'CS2002', title: 'Databases' }),
      makeModule({ code: 'MA1001', title: 'Calculus' }),
      makeModule({ code: 'PH1001', title: 'Waves', isActive: false }),
      makeModule({ code: 'PH2001', title: 'Optics', enrolledCount: 30 }),
      makeModule({
        code: 'EE1001',
        title: 'Circuits',
        semester: 'spring',
        enrolmentState: 'notYetOpen',
        windowOpensAt: '2027-01-04T09:00:00.000Z',
      }),
      makeModule({ code: 'EE2001', title: 'Control', enrolmentState: 'closed' }),
      makeModule({
        code: 'EE3001',
        title: 'Power',
        semester: 'spring',
        enrolmentState: 'noWindow',
        windowOpensAt: null,
        windowClosesAt: null,
      }),
      makeModule({ code: 'MA2001', title: 'Statistics', credits: 30 }),
      makeModule(),
    ]
    const rows = [
      makeMyEnrolment({ moduleCode: 'CS3001' }),
      makeMyEnrolment({
        moduleCode: 'CS3002',
        canWithdraw: false,
        withdrawBlockedReason: 'results',
      }),
      makeMyEnrolment({
        moduleCode: 'CS3003',
        canWithdraw: false,
        withdrawBlockedReason: 'deadline',
        withdrawalDeadlineAt: '2026-09-20T16:00:00.000Z',
      }),
      makeMyEnrolment({
        moduleCode: 'CS2001',
        academicYear: '2025/26',
        canWithdraw: false,
        withdrawBlockedReason: 'year',
      }),
      makeMyEnrolment({
        moduleCode: 'CS2002',
        academicYear: '2025/26',
        canWithdraw: false,
        withdrawBlockedReason: 'year',
      }),
      makeMyEnrolment({
        moduleCode: 'MA1001',
        status: 'withdrawn',
        withdrawnAt: '2026-09-20T10:00:00.000Z',
        canWithdraw: false,
      }),
    ]
    const queryClient = createTestQueryClient()
    queryClient.setQueryData(
      queryKeys.student.dashboard,
      makeDashboard({ completed: [makeCompleted()] }),
    )

    renderRoutes(studentRoutes, {
      route: '/student/modules',
      queryClient,
      handlers: [catalogueHandler(modules), enrolmentsHandler(rows)],
    })
    await screen.findByRole('link', { name: /^CS3099/ })

    // Active this year and withdrawable.
    let item = card('CS3001')
    expect(within(item).getByText('Enrolled')).toBeInTheDocument()
    expect(within(item).getByRole('button', { name: 'Withdraw from CS3001' })).toBeEnabled()
    expect(
      within(item).getByText('Withdrawal deadline 30 October 2026 at 17:00 (GMT)'),
    ).toBeInTheDocument()

    item = card('CS3002')
    expect(within(item).getByText('Enrolled')).toBeInTheDocument()
    expect(within(item).queryByRole('button', { name: /Withdraw/ })).not.toBeInTheDocument()
    expect(item).toHaveTextContent('Marks recorded; to withdraw, contact the academic office.')

    item = card('CS3003')
    expect(
      within(item).getByText('Withdrawal deadline passed 20 September 2026 at 17:00 (BST).'),
    ).toBeInTheDocument()

    item = card('CS2001')
    expect(within(item).getByText('Completed 2025/26')).toBeInTheDocument()
    expect(within(item).getByText('Mark 68 (2:1)')).toBeInTheDocument()
    expect(within(card('CS2002')).getByText('Result not yet published')).toBeInTheDocument()

    item = card('MA1001')
    expect(within(item).getByRole('button', { name: 'Enrol again on MA1001' })).toBeEnabled()
    expect(within(item).getByText('Withdrawn 20 September 2026')).toBeInTheDocument()

    item = card('PH1001')
    expect(within(item).getByRole('button', { name: 'Not running' })).toBeDisabled()
    expect(within(item).getByText('PH1001 is no longer running.')).toBeInTheDocument()

    item = card('PH2001')
    expect(within(item).getByRole('button', { name: 'Full' })).toBeDisabled()
    expect(
      within(item).getByText(
        'Full. Places free up when students withdraw; there is no waiting list yet.',
      ),
    ).toBeInTheDocument()
    expect(within(item).getByRole('meter')).toHaveAttribute('aria-valuetext', '0 of 30 places left')

    item = card('EE1001')
    expect(within(item).getByRole('button', { name: 'Not open yet' })).toBeDisabled()
    expect(
      within(item).getByText('Enrolment for Spring opens 4 January 2027 at 09:00 (GMT).'),
    ).toBeInTheDocument()

    item = card('EE2001')
    expect(within(item).getByRole('button', { name: 'Enrolment closed' })).toBeDisabled()
    expect(
      within(item).getByText('Enrolment for Autumn closed on 2 October 2026 at 18:00 (BST).'),
    ).toBeInTheDocument()

    item = card('EE3001')
    expect(within(item).getByRole('button', { name: 'Enrolment closed' })).toBeDisabled()
    expect(
      within(item).getByText('Enrolment dates for Spring have not been announced yet.'),
    ).toBeInTheDocument()

    // 45 autumn credits already: a 30-credit module would pass 60.
    item = card('MA2001')
    expect(within(item).getByRole('button', { name: 'Over credit limit' })).toBeDisabled()
    expect(
      within(item).getByText('That would take you over 60 credits for Autumn.'),
    ).toBeInTheDocument()

    item = card('CS3099')
    const enrol = within(item).getByRole('button', { name: 'Enrol on CS3099' })
    expect(enrol).toBeEnabled()
    expect(enrol).toHaveAccessibleDescription('12 places left')
    expect(within(item).getByRole('meter')).toHaveAttribute(
      'aria-valuetext',
      '12 of 30 places left',
    )
  })

  it('links to the results page for a completed module when the dashboard is not loaded', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/modules',
      handlers: [
        catalogueHandler([makeModule({ code: 'CS2001', title: 'Algorithms' })]),
        enrolmentsHandler([
          makeMyEnrolment({
            moduleCode: 'CS2001',
            academicYear: '2025/26',
            canWithdraw: false,
            withdrawBlockedReason: 'year',
          }),
        ]),
      ],
    })
    const item = await screen.findByRole('link', { name: /^CS2001/ })
    const scope = within(item.closest('li') as HTMLElement)
    expect(scope.getByText('Completed 2025/26')).toBeInTheDocument()
    expect(scope.getByRole('link', { name: 'Results page' })).toHaveAttribute(
      'href',
      '/student/results',
    )
  })

  it('enrols through the server and shows the new state and count', async () => {
    const server = createEnrolmentServer({ modules: makeCatalogue(), rows: [makeMyEnrolment()] })
    const { events } = renderRoutes(studentRoutes, {
      route: '/student/modules?q=CS3099',
      handlers: server.handlers,
    })
    await events.click(await screen.findByRole('button', { name: 'Enrol on CS3099' }))
    expect(await screen.findByText("You're in: CS3099. 11 places left.")).toBeInTheDocument()
    const item = card('CS3099')
    await waitFor(() => expect(within(item).getByText('Enrolled')).toBeInTheDocument())
    expect(within(item).getByRole('meter')).toHaveAttribute(
      'aria-valuetext',
      '11 of 30 places left',
    )
    expect(server.state.requests.enrol).toBe(1)
  })

  it('offers Retry when the catalogue cannot load', async () => {
    renderRoutes(studentRoutes, {
      route: '/student/modules',
      handlers: [http.get('/api/modules', () => problem('internal-error')), enrolmentsHandler([])],
    })
    expect(await screen.findByRole('button', { name: /Try again/ })).toBeInTheDocument()
    expect(screen.getByText(/Something went wrong on our side/)).toBeInTheDocument()
  })
})
