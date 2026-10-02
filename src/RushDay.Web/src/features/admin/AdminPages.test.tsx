import { configure, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'

import { makeAdminMe, makeAnnouncement, makeStudentMe } from '@/test/factories'
import {
  createAdminMock,
  makeAdminLecturer,
  makeAdminMarksRow,
  makeAdminMarksSheet,
  makeAdminModule,
  makeAdminStudentRow,
  makeAdminStudentView,
  makeMarksStatus,
  makeRosterEntry,
} from '@/test/handlers/admin'
import { renderRoutes } from '@/test/render'

import { adminRoutes } from './routes'

// Admin pages render large lazy route trees; under coverage on a CI runner the first render
// can take longer than the default second.
configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 30_000 })

beforeAll(async () => {
  await Promise.all([
    import('./AdminOverviewPage'),
    import('./StudentsPage'),
    import('./StudentSupportPage'),
    import('./ModulesAdminPage'),
    import('./ModuleAdminPage'),
    import('./LecturersPage'),
    import('./EnrolmentWindowsPage'),
    import('./AnnouncementsAdminPage'),
  ])
})

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-09-29T12:00:00.000Z'))
})

afterEach(() => {
  vi.useRealTimers()
})

function render(route: string, mock = createAdminMock()) {
  const result = renderRoutes(adminRoutes, { route, user: makeAdminMe(), handlers: mock.handlers })
  return { ...result, mock }
}

describe('admin area', () => {
  it('is only for administrators', async () => {
    const { router } = renderRoutes(
      [...adminRoutes, { path: '/forbidden', element: <h1>Forbidden</h1> }],
      { route: '/admin', user: makeStudentMe() },
    )
    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})

describe('AdminOverviewPage', () => {
  it('shows the counts, windows, results progress, quick actions and recent activity', async () => {
    render('/admin')
    expect(await screen.findByRole('heading', { name: 'Overview', level: 1 })).toBeInTheDocument()
    expect(await screen.findByRole('group', { name: 'Students' })).toHaveTextContent('20,000')
    expect(screen.getByRole('group', { name: 'Locked accounts' })).toHaveTextContent('2')
    expect(screen.getByText('Results: 2 of 12 modules submitted for Autumn')).toBeInTheDocument()
    expect(screen.getByText('Open')).toBeInTheDocument()
    expect(screen.getByText('Database answering normally')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Provision account' })).toHaveAttribute(
      'href',
      '/admin/accounts?provision=1',
    )
    expect(screen.getByRole('link', { name: 'New announcement' })).toHaveAttribute(
      'href',
      '/admin/announcements?new=1',
    )
    expect(screen.getByRole('table', { name: 'Recent activity' })).toHaveTextContent(
      'Results published',
    )
  })
})

describe('StudentsPage', () => {
  it('searches, pages and links to the support view', async () => {
    const mock = createAdminMock({
      students: [
        makeAdminStudentRow(),
        makeAdminStudentRow({
          studentNumber: 'S000009',
          fullName: 'Dana Evans',
          accountState: 'none',
          leftAt: '2026-09-01T09:00:00.000Z',
        }),
      ],
    })
    const { events, router } = render('/admin/students', mock)
    expect(await screen.findByText('Dana Evans')).toBeInTheDocument()
    expect(screen.getByText('Left')).toBeInTheDocument()
    expect(screen.getByText('No account')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'View S000002 Ben Carter' })).toHaveAttribute(
      'href',
      '/admin/students/S000002',
    )
    await events.type(screen.getByLabelText('Search students'), 'zzz')
    expect(await screen.findByText('No students match "zzz".')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?q=zzz')
  })

  it('creates a student and provisions the account in one go', async () => {
    const mock = createAdminMock()
    const { events } = render('/admin/students?create=1', mock)
    const dialog = await screen.findByRole('dialog', { name: 'Create student' })
    await events.type(within(dialog).getByLabelText(/^Student number/), 's000123')
    await events.type(within(dialog).getByLabelText(/^Full name/), 'Ada Lovelace')
    await events.type(within(dialog).getByLabelText(/^Programme/), 'BSc Mathematics')
    expect(within(dialog).getByRole('checkbox', { name: 'Provision an account now' })).toBeChecked()
    await events.click(within(dialog).getByRole('button', { name: 'Create student' }))

    const password = await screen.findByRole('dialog', { name: 'Temporary password for S000123' })
    expect(within(password).getByText('Kx7mPq2RtW9vNz4H')).toBeInTheDocument()
    expect(mock.calls('POST', '/api/admin/students')[0]?.body).toEqual({
      studentNumber: 'S000123',
      fullName: 'Ada Lovelace',
      programme: 'BSc Mathematics',
      yearOfStudy: 1,
      email: null,
    })
    expect(mock.calls('POST', '/api/admin/accounts')[0]?.body).toMatchObject({
      username: 'S000123',
      role: 'Student',
      studentNumber: 'S000123',
    })
  })

  it('names a taken student number on the field', async () => {
    const { events } = render('/admin/students?create=1')
    const dialog = await screen.findByRole('dialog', { name: 'Create student' })
    await events.type(within(dialog).getByLabelText(/^Student number/), 'S000002')
    await events.type(within(dialog).getByLabelText(/^Full name/), 'Someone')
    await events.type(within(dialog).getByLabelText(/^Programme/), 'BSc')
    await events.click(within(dialog).getByRole('button', { name: 'Create student' }))
    expect(await within(dialog).findByText('S000002 already exists.')).toBeInTheDocument()
  })
})

describe('StudentSupportPage', () => {
  it('shows the record as the student sees it, with statuses and the audit banner', async () => {
    const view = makeAdminStudentView({
      grades: [
        {
          ...makeAdminStudentView().grades[0]!,
          correctedAt: '2026-09-20T10:00:00.000Z',
        },
        {
          moduleCode: 'CS3001',
          moduleTitle: 'Distributed Systems',
          credits: 15,
          semester: 'autumn',
          academicYear: '2026/27',
          outcome: 'mark',
          mark: 61,
          status: 'published',
          publishedAt: '2026-10-05T08:00:00.000Z',
          visibleToStudent: false,
          version: 2,
          correctedAt: null,
        },
      ],
    })
    const mock = createAdminMock({ studentViews: { S000002: view } })
    render('/admin/students/S000002', mock)
    expect(await screen.findByRole('heading', { name: 'Ben Carter', level: 1 })).toBeInTheDocument()
    expect(
      screen.getByText('Viewing as administrator. This view is recorded in the audit log.'),
    ).toBeInTheDocument()
    const marks = screen.getByRole('table', { name: 'Marks of S000002' })
    expect(within(marks).getByText('Published')).toBeInTheDocument()
    expect(within(marks).getByText('Scheduled')).toBeInTheDocument()
    expect(within(marks).getByText('Visible')).toBeInTheDocument()
    expect(within(marks).getByText('Hidden')).toBeInTheDocument()
    expect(within(marks).getByText(/Amended 20 Sept? 2026/)).toBeInTheDocument()
    expect(screen.getByText(/Average as the student sees it:/).parentElement).toHaveTextContent(
      'Average as the student sees it: 68.0 · indicative band 2:1',
    )
    expect(
      screen.getByRole('button', { name: 'Export data (JSON), downloads a file' }),
    ).toBeInTheDocument()
  })

  it('marks a student as left with the consequence and a reason', async () => {
    const mock = createAdminMock()
    const { events } = render('/admin/students/S000002', mock)
    await events.click(await screen.findByRole('button', { name: 'Mark as left' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Mark Ben Carter as left?' })
    expect(dialog).toHaveTextContent('Withdraws 1 enrolment this year and disables the account.')
    await events.type(within(dialog).getByLabelText(/^Reason/), 'Formal withdrawal on 28 September')
    await events.click(within(dialog).getByRole('button', { name: 'Mark as left' }))
    expect(
      await screen.findByText(/S000002 is marked as left\. 1 enrolment withdrawn\./),
    ).toBeInTheDocument()
    expect(mock.calls('POST', '/api/admin/students/S000002/leave')[0]?.body).toEqual({
      reason: 'Formal withdrawal on 28 September',
    })
  })

  it('override-enrols with a reason and warns when the module is already submitted', async () => {
    const mock = createAdminMock({
      modules: [
        makeAdminModule({
          code: 'CS3099',
          placesRemaining: 0,
          capacity: 30,
          enrolledCount: 30,
          marks: makeMarksStatus({ status: 'submitted' }),
        }),
      ],
    })
    const { events } = render('/admin/students/S000002', mock)
    await events.click(await screen.findByRole('button', { name: 'Enrol on a module' }))
    const dialog = await screen.findByRole('dialog', { name: 'Enrol Ben Carter on a module' })
    await events.type(within(dialog).getByRole('combobox', { name: /^Module/ }), 'CS30')
    await events.click(await within(dialog).findByRole('option', { name: /CS3099/ }))
    expect(
      within(dialog).getByText(
        'Marks for CS3099 are already submitted. The lecturer will need the module returned to draft to enter a mark for this student.',
      ),
    ).toBeInTheDocument()
    await events.type(within(dialog).getByLabelText(/^Reason/), 'Late registration approved')
    await events.click(within(dialog).getByRole('button', { name: 'Enrol Ben' }))
    expect(
      await within(dialog).findByText(
        'CS3099 is full. Tick "Raise capacity by one if full" to enrol them anyway.',
      ),
    ).toBeInTheDocument()
    await events.click(
      within(dialog).getByRole('checkbox', { name: 'Raise capacity by one if full' }),
    )
    await events.click(within(dialog).getByRole('button', { name: 'Enrol Ben' }))
    expect(
      await screen.findByText(
        /Enrolled S000002 on CS3099\. 0 places left\. Capacity raised by one\./,
      ),
    ).toBeInTheDocument()
    expect(mock.calls('POST', '/api/admin/students/S000002/enrolments').at(-1)?.body).toEqual({
      moduleCode: 'CS3099',
      reason: 'Late registration approved',
      forceCapacity: true,
    })
  })

  it('override-withdraws and corrects a mark from the record', async () => {
    const mock = createAdminMock({
      marks: {
        CS2001: makeAdminMarksSheet({
          code: 'CS2001',
          rows: [makeAdminMarksRow(1, { gradeStatus: 'published', mark: 68 })],
        }),
      },
    })
    const { events } = render('/admin/students/S000002', mock)
    await events.click(await screen.findByRole('button', { name: 'Withdraw from CS3001' }))
    const withdraw = await screen.findByRole('alertdialog', {
      name: 'Withdraw Ben Carter from CS3001?',
    })
    await events.type(within(withdraw).getByLabelText(/^Reason/), 'Moved to part-time study')
    await events.click(within(withdraw).getByRole('button', { name: 'Withdraw' }))
    expect(await screen.findByText('Withdrew S000002 from CS3001.')).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Correct mark for CS2001' }))
    const correct = await screen.findByRole('alertdialog', {
      name: "Correct Ben Carter's mark for CS2001",
    })
    expect(
      within(correct).getByText('The student sees the corrected mark immediately.'),
    ).toBeInTheDocument()
  })

  it('says a malformed or unknown student number does not exist', async () => {
    render('/admin/students/S999999')
    expect(await screen.findByText("That page or record doesn't exist.")).toBeInTheDocument()
  })
})

describe('ModulesAdminPage and ModuleEditDialog', () => {
  const modules = [
    makeAdminModule({ code: 'CS3099', enrolledCount: 18, capacity: 30 }),
    makeAdminModule({
      code: 'MA1001',
      title: 'Calculus',
      semester: 'spring',
      enrolledCount: 0,
      placesRemaining: 40,
      capacity: 40,
      lecturers: [],
    }),
  ]

  it('filters and sorts in the browser', async () => {
    const { events } = render('/admin/modules', createAdminMock({ modules }))
    expect(await screen.findByText('2 of 2 modules')).toBeInTheDocument()
    const sort = screen.getByRole('button', { name: 'Sort by Code, currently sorted ascending' })
    await events.click(sort)
    expect(screen.getByRole('columnheader', { name: /Code/ })).toHaveAttribute(
      'aria-sort',
      'descending',
    )
    await events.selectOptions(screen.getByLabelText('Semester'), 'spring')
    expect(await screen.findByText('1 of 2 modules')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'MA1001' })).toBeInTheDocument()
  })

  it('warns about credits and capacity and locks the semester while students are enrolled', async () => {
    const mock = createAdminMock({ modules })
    const { events } = render('/admin/modules', mock)
    await events.click(await screen.findByRole('button', { name: 'Edit CS3099' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit CS3099' })
    expect(within(dialog).getByLabelText(/^Semester/)).toBeDisabled()
    expect(
      within(dialog).getByText(
        "Can't change: 18 students are enrolled this year. Create a new module instead.",
      ),
    ).toBeInTheDocument()

    const credits = within(dialog).getByLabelText(/^Credits/)
    await events.clear(credits)
    await events.type(credits, '30')
    expect(
      within(dialog).getByText("Changing credits affects 18 enrolled students' budgets"),
    ).toBeInTheDocument()
    const capacity = within(dialog).getByLabelText(/^Capacity/)
    await events.clear(capacity)
    await events.type(capacity, '10')
    expect(within(dialog).getByText('Below current enrolment (18)')).toBeInTheDocument()
    await events.click(within(dialog).getByRole('button', { name: 'Save changes' }))
    expect(
      await within(dialog).findByText("Capacity can't go below the 18 students already enrolled."),
    ).toBeInTheDocument()

    await events.clear(capacity)
    await events.type(capacity, '35')
    await events.click(within(dialog).getByRole('button', { name: 'Save changes' }))
    expect(await screen.findByText('Saved CS3099.')).toBeInTheDocument()
    expect(mock.calls('PUT', '/api/admin/modules/CS3099').at(-1)?.body).toMatchObject({
      credits: 30,
      capacity: 35,
      semester: 'autumn',
      isActive: true,
    })
  })

  it('creates a module', async () => {
    const mock = createAdminMock({ modules })
    const { events } = render('/admin/modules', mock)
    await events.click(await screen.findByRole('button', { name: 'New module' }))
    const dialog = await screen.findByRole('dialog', { name: 'New module' })
    await events.type(within(dialog).getByLabelText(/^Module code/), 'cs4001')
    await events.type(within(dialog).getByLabelText(/^Title/), 'Advanced Topics')
    await events.click(within(dialog).getByRole('button', { name: 'Create module' }))
    expect(await screen.findByText('Created CS4001.')).toBeInTheDocument()
    expect(mock.calls('POST', '/api/admin/modules')[0]?.body).toEqual({
      code: 'CS4001',
      title: 'Advanced Topics',
      description: null,
      credits: 15,
      capacity: 30,
      semester: 'autumn',
    })
  })
})

describe('ModuleAdminPage', () => {
  it('assigns lecturers with exactly one leader', async () => {
    const mock = createAdminMock({
      modules: [makeAdminModule({ code: 'CS3099' })],
      lecturers: [
        makeAdminLecturer(),
        makeAdminLecturer({ staffNumber: 'L00006', fullName: 'Tom Price', moduleCodes: [] }),
        makeAdminLecturer({
          staffNumber: 'L00009',
          fullName: 'Gone Away',
          leftAt: '2026-01-01T00:00:00.000Z',
        }),
      ],
    })
    const { events } = render('/admin/modules/CS3099', mock)
    const editor = await screen.findByRole('list', { name: 'Lecturers of CS3099' })
    expect(within(editor).getByRole('combobox', { name: 'Lecturer 1' })).toHaveValue(
      'L00001 Dr Grace Hopper',
    )

    await events.click(screen.getByRole('button', { name: 'Add lecturer' }))
    const second = screen.getByRole('combobox', { name: 'Lecturer 2' })
    await events.type(second, 'L0000')
    expect(await screen.findByRole('option', { name: /L00006 Dr Tom Price/ })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /Gone Away/ })).not.toBeInTheDocument()
    await events.click(screen.getByRole('option', { name: /L00006 Dr Tom Price/ }))

    await events.selectOptions(screen.getByLabelText('Role of lecturer 2'), 'leader')
    await events.click(screen.getByRole('button', { name: 'Save lecturers' }))
    expect(await screen.findByText('Only one leader is allowed; 2 are chosen.')).toBeInTheDocument()
    expect(mock.calls('PUT', '/api/admin/modules/CS3099/lecturers')).toHaveLength(0)

    await events.selectOptions(screen.getByLabelText('Role of lecturer 2'), 'teacher')
    await events.click(screen.getByRole('button', { name: 'Save lecturers' }))
    expect(await screen.findByText('Saved the lecturers of CS3099.')).toBeInTheDocument()
    expect(mock.calls('PUT', '/api/admin/modules/CS3099/lecturers')[0]?.body).toEqual({
      assignments: [
        { staffNumber: 'L00001', role: 'leader' },
        { staffNumber: 'L00006', role: 'teacher' },
      ],
    })
  })

  it('offers Trim to capacity on an oversold module', async () => {
    const mock = createAdminMock({
      modules: [
        makeAdminModule({ code: 'CS3099', capacity: 30, enrolledCount: 154, placesRemaining: 0 }),
      ],
    })
    const { events } = render('/admin/modules/CS3099', mock)
    expect(
      await screen.findByText(/CS3099 has 154 students for 30 places: 124 over capacity\./),
    ).toBeInTheDocument()
    await events.click(screen.getByRole('button', { name: 'Trim to capacity' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Trim CS3099 to capacity?' })
    expect(dialog).toHaveTextContent(
      'Withdraws the 124 most recent enrolments so CS3099 has 30 students. Each is recorded in the audit log.',
    )
    await events.type(within(dialog).getByLabelText(/^Reason/), 'Oversold by the v0 portal')
    await events.click(within(dialog).getByRole('button', { name: 'Withdraw 124 enrolments' }))
    expect(await screen.findByText(/CS3099 trimmed from 154 to 30 students\./)).toBeInTheDocument()
  })

  it('shows the read-only roster', async () => {
    const mock = createAdminMock({
      modules: [makeAdminModule({ code: 'CS3099' })],
      rosters: {
        CS3099: [makeRosterEntry(0), makeRosterEntry(1, { status: 'withdrawn' })],
      },
    })
    render('/admin/modules/CS3099/roster', mock)
    const table = await screen.findByRole('table', { name: 'Students on CS3099' })
    expect(within(table).getByText('Student 1')).toBeInTheDocument()
    expect(within(table).getByText('Withdrawn')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Roster' })).toHaveAttribute('aria-current', 'page')
  })

  it('shows the read-only marks with Correct on submitted rows only', async () => {
    const mock = createAdminMock({
      modules: [makeAdminModule({ code: 'CS3001' })],
      marks: {
        CS3001: makeAdminMarksSheet({
          rows: [
            makeAdminMarksRow(0),
            makeAdminMarksRow(1, { gradeStatus: 'draft' }),
            makeAdminMarksRow(2, {
              outcome: 'absent',
              mark: null,
              correctedAt: '2026-09-26T10:00:00.000Z',
            }),
          ],
        }),
      },
    })
    const { events } = render('/admin/modules/CS3001/marks', mock)
    const table = await screen.findByRole('table', { name: 'Marks for CS3001, read-only' })
    expect(within(table).queryByRole('textbox')).not.toBeInTheDocument()
    expect(within(table).getAllByRole('button', { name: /^Correct the mark of/ })).toHaveLength(2)
    expect(within(table).getByText('Absent')).toBeInTheDocument()
    expect(within(table).getByText(/Amended 26 Sept? 2026/)).toBeInTheDocument()
    await events.click(
      within(table).getByRole('button', { name: 'Correct the mark of S000001 Student 1' }),
    )
    expect(
      await screen.findByRole('alertdialog', { name: "Correct Student 1's mark for CS3001" }),
    ).toBeInTheDocument()
  })

  it('says an unknown module does not exist', async () => {
    render('/admin/modules/ZZ9999')
    expect(await screen.findByText("That page or record doesn't exist.")).toBeInTheDocument()
  })
})

describe('LecturersPage', () => {
  it('lists, creates, edits and marks lecturers as left', async () => {
    const mock = createAdminMock()
    const { events } = render('/admin/lecturers', mock)
    expect(await screen.findByText('Aisha Khan')).toBeInTheDocument()
    expect(screen.getByText('No account')).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Create lecturer' }))
    const create = await screen.findByRole('dialog', { name: 'Create lecturer' })
    await events.type(within(create).getByLabelText(/^Staff number/), 'l00041')
    await events.type(within(create).getByLabelText(/^Full name/), 'Nia Owens')
    await events.type(within(create).getByLabelText(/^Department/), 'ma')
    await events.click(within(create).getByRole('button', { name: 'Create lecturer' }))
    expect(await screen.findByText('Created L00041 Dr Nia Owens.')).toBeInTheDocument()
    expect(mock.calls('POST', '/api/admin/lecturers')[0]?.body).toEqual({
      staffNumber: 'L00041',
      fullName: 'Nia Owens',
      title: 'Dr',
      department: 'MA',
      email: null,
    })

    await events.click(screen.getByRole('button', { name: 'Edit L00006' }))
    const edit = await screen.findByRole('dialog', { name: 'Edit L00006' })
    await events.selectOptions(within(edit).getByLabelText(/^Title/), 'Prof')
    await events.click(within(edit).getByRole('button', { name: 'Save changes' }))
    expect(await screen.findByText('Saved L00006.')).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Mark L00006 as left' }))
    const leave = await screen.findByRole('alertdialog')
    expect(leave).toHaveTextContent(
      'Disables the account; their module assignments stay and show as left.',
    )
    await events.type(within(leave).getByLabelText(/^Reason/), 'Retired at the end of August')
    await events.click(within(leave).getByRole('button', { name: 'Mark as left' }))
    expect(await screen.findByText('L00006 is marked as left.')).toBeInTheDocument()
  })
})

describe('EnrolmentWindowsPage', () => {
  it('edits a window in the institution zone and checks the dates first', async () => {
    const mock = createAdminMock()
    const { events } = render('/admin/enrolment', mock)
    const card = (await screen.findByRole('heading', { name: '2026/27 Autumn' })).closest(
      'section',
    )!
    const opens = within(card).getByLabelText(/^Opens \(Europe\/London\)/)
    expect(opens).toHaveValue('2026-09-14T10:00')
    const closes = within(card).getByLabelText(/^Closes \(Europe\/London\)/)
    fireEvent.change(closes, { target: { value: '2026-09-01T10:00' } })
    await events.click(within(card).getByRole('button', { name: 'Save' }))
    expect(await within(card).findByText('Closes must be after opens.')).toBeInTheDocument()

    fireEvent.change(closes, { target: { value: '2026-10-09T17:00' } })
    expect(within(card).getByText('Reads as 9 October 2026 at 17:00 (BST).')).toBeInTheDocument()
    await events.click(within(card).getByRole('button', { name: 'Save' }))
    expect(await screen.findByText('Saved the 2026/27 Autumn window.')).toBeInTheDocument()
    expect(
      mock.calls('PUT', `/api/admin/enrolment-windows/${mock.state.windows[0]!.id}`)[0]?.body,
    ).toEqual({
      opensAt: '2026-09-14T09:00:00.000Z',
      closesAt: '2026-10-09T16:00:00.000Z',
      withdrawalDeadlineAt: '2026-10-30T17:00:00.000Z',
    })
  })

  it('closes an open window only after the warning', async () => {
    const mock = createAdminMock()
    const { events } = render('/admin/enrolment', mock)
    await events.click(await screen.findByRole('button', { name: 'Close now' }))
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog).toHaveTextContent(
      'Students who are enrolling right now will see Enrolment closed. Continue?',
    )
    await events.click(within(dialog).getByRole('button', { name: 'Close now' }))
    expect(await screen.findByText('2026/27 Autumn is closed.')).toBeInTheDocument()
    expect(
      mock.calls('PUT', `/api/admin/enrolment-windows/${mock.state.windows[0]!.id}`)[0]?.body,
    ).toMatchObject({
      closesAt: '2026-09-29T12:00:00.000Z',
    })
  })

  it('adds a window and reports one that already exists', async () => {
    const mock = createAdminMock()
    const { events } = render('/admin/enrolment', mock)
    await events.click(await screen.findByRole('button', { name: 'Add window' }))
    const card = screen.getByRole('heading', { name: 'New window' }).closest('section')!
    expect(within(card).getByLabelText(/^Academic year/)).toHaveValue('2026/27')
    fireEvent.change(within(card).getByLabelText(/^Opens/), {
      target: { value: '2026-09-14T09:00' },
    })
    fireEvent.change(within(card).getByLabelText(/^Closes/), {
      target: { value: '2026-10-02T17:00' },
    })
    fireEvent.change(within(card).getByLabelText(/^Withdrawal deadline/), {
      target: { value: '2026-10-30T17:00' },
    })
    await events.click(within(card).getByRole('button', { name: 'Add window' }))
    expect(
      await within(card).findByText('A window for 2026/27 Autumn already exists. Edit it instead.'),
    ).toBeInTheDocument()

    await events.selectOptions(within(card).getByLabelText(/^Semester/), 'spring')
    await events.click(within(card).getByRole('button', { name: 'Add window' }))
    expect(await screen.findByText('Saved the 2026/27 Spring window.')).toBeInTheDocument()
  })
})

describe('AnnouncementsAdminPage', () => {
  it('lists every scope with badges and posts a new university announcement', async () => {
    const mock = createAdminMock({
      announcements: [
        makeAnnouncement({ id: 'a1', pinned: true, title: 'Results are out' }),
        makeAnnouncement({
          id: 'a2',
          scope: 'module',
          moduleCode: 'CS3001',
          title: 'Lab moved',
          publishedAt: '2026-10-10T09:00:00.000Z',
        }),
      ],
    })
    const { events } = render('/admin/announcements?new=1', mock)
    const dialog = await screen.findByRole('dialog', { name: 'New university announcement' })
    await events.type(within(dialog).getByLabelText(/^Title/), 'Library hours')
    await events.type(within(dialog).getByLabelText(/^Message/), 'Open until 22:00.')
    expect(within(dialog).getByText(/17 of 4,000 characters/)).toBeInTheDocument()
    await events.click(within(dialog).getByRole('button', { name: 'Post announcement' }))
    expect(await screen.findByText('Announcement posted.')).toBeInTheDocument()
    expect(mock.calls('POST', '/api/admin/announcements')[0]?.body).toEqual({
      title: 'Library hours',
      body: 'Open until 22:00.',
      pinned: false,
    })

    const moduleNotice = screen.getByRole('article', { name: 'Lab moved' })
    expect(within(moduleNotice).getByText('CS3001')).toBeInTheDocument()
    expect(within(moduleNotice).getByText('Not published yet')).toBeInTheDocument()
    expect(
      within(screen.getByRole('article', { name: 'Results are out' })).getByText('Pinned'),
    ).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Delete Lab moved' }))
    const confirm = await screen.findByRole('alertdialog')
    await events.click(within(confirm).getByRole('button', { name: 'Delete announcement' }))
    expect(await screen.findByText('Announcement deleted.')).toBeInTheDocument()
    expect(mock.calls('DELETE', '/api/admin/announcements/a2')).toHaveLength(1)
  })
})
