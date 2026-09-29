import { configure, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'

import { makeAdminMe } from '@/test/factories'
import {
  createAdminMock,
  makeAdminResults,
  makePublicationInfo,
  makeResultsModule,
} from '@/test/handlers/admin'
import { renderRoutes } from '@/test/render'

import { adminRoutes } from './routes'

// Admin pages render large lazy route trees; under coverage on a CI runner the first render
// can take longer than the default second.
configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 30_000 })

const NOW = '2026-09-29T12:00:00.000Z'

beforeAll(async () => {
  await import('./ResultsAdminPage')
})

beforeEach(() => {
  // Only Date is faked: the picker's "now" and its 90-day maximum are then fixed, while timers,
  // user-event and MSW keep running in real time.
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date(NOW))
})

afterEach(() => {
  vi.useRealTimers()
})

function render(mock = createAdminMock()) {
  const result = renderRoutes(adminRoutes, {
    route: '/admin/results',
    user: makeAdminMe(),
    handlers: mock.handlers,
  })
  return { ...result, mock }
}

async function openPublish(events: ReturnType<typeof render>['events']) {
  await events.click(await screen.findByRole('button', { name: 'Publish results' }))
  return screen.findByRole('alertdialog', { name: 'Publish Autumn 2026/27 results' })
}

describe('ResultsAdminPage', () => {
  it('shows submission progress with the next step for each state', async () => {
    const mock = createAdminMock({
      results: [
        makeAdminResults({
          modules: [
            makeResultsModule('CS3001', {
              status: 'submitted',
              entered: 100,
              missing: 0,
              total: 100,
            }),
            makeResultsModule('CS3099', {
              status: 'submitted',
              entered: 28,
              missing: 2,
              total: 30,
            }),
            makeResultsModule('MA1001', { status: 'draft', entered: 0, missing: 40, total: 40 }),
            makeResultsModule('EE1001', {
              status: 'published',
              entered: 10,
              missing: 0,
              total: 10,
              publishedAt: '2026-09-28T08:00:00.000Z',
            }),
            makeResultsModule('PH2001', { status: 'noStudents', missing: 0, total: 0 }),
          ],
        }),
      ],
    })
    const { events } = render(mock)
    const table = await screen.findByRole('table', {
      name: 'Submission progress for Autumn 2026/27',
    })
    expect(within(table).getByText('Ready to publish')).toBeInTheDocument()
    expect(
      within(table).getByText(/Submitted, 2 marks missing \(a student without a submitted mark\):/),
    ).toBeInTheDocument()
    expect(within(table).getByText('Waiting for the lecturer')).toBeInTheDocument()
    expect(within(table).getByText(/Live: correct single marks from the/)).toBeInTheDocument()
    // Modules without students are hidden by default.
    expect(within(table).queryByText('PH2001')).not.toBeInTheDocument()
    await events.click(screen.getByRole('checkbox', { name: 'Hide modules without students' }))
    expect(await within(table).findByText('PH2001')).toBeInTheDocument()
    const codes = within(table)
      .getAllByRole('link')
      .map((link) => link.textContent)
      .filter((text) => /^[A-Z]{2}\d{4}$/.test(text ?? ''))
    expect(codes).toEqual(['CS3001', 'CS3099', 'MA1001', 'EE1001', 'PH2001'])
  })

  it('previews what will be published and lists the excluded modules with reasons', async () => {
    const { events } = render()
    const dialog = await openPublish(events)
    expect(
      within(dialog).getByText('Will publish 1 module (100 marks); 2 modules excluded'),
    ).toBeInTheDocument()
    const excluded = within(dialog).getByRole('list', { name: 'Excluded, and why:' })
    expect(
      within(excluded)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['CS30992 marks missing', 'MA1001not submitted'])
    expect(
      within(dialog).getByText(
        'This is the results-day button. Students see these marks at the chosen instant.',
      ),
    ).toBeInTheDocument()
    expect(
      within(dialog).getByRole('checkbox', {
        name: 'Post a pinned university announcement: Autumn 2026/27 results are available',
      }),
    ).toBeChecked()
  })

  it('keeps the button disabled until the exam board approval is ticked, then publishes now', async () => {
    const { events, mock } = render()
    const dialog = await openPublish(events)
    const publish = within(dialog).getByRole('button', { name: 'Publish 1 module now' })
    expect(publish).toBeDisabled()

    await events.click(
      within(dialog).getByRole('checkbox', { name: 'The exam board has approved these marks' }),
    )
    expect(publish).toBeEnabled()
    await events.click(publish)

    expect(
      await screen.findByText('Published: 1 module, 100 marks. Students see them now.'),
    ).toBeInTheDocument()
    expect(mock.calls('POST', '/api/admin/results/publish')[0]?.body).toEqual({
      academicYear: '2026/27',
      semester: 'autumn',
      publishAt: NOW,
      announce: true,
    })
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    const history = screen.getByRole('table', { name: 'Publication history' })
    expect(within(history).getByText('Live')).toBeInTheDocument()
  })

  it('schedules in the institution zone, named, at most 90 days ahead', async () => {
    const { events, mock } = render()
    const dialog = await openPublish(events)
    await events.click(within(dialog).getByRole('checkbox', { name: 'Publish now' }))

    const picker = within(dialog).getByLabelText(/^Students see the marks at \(Europe\/London\)/)
    // Defaults to the next 09:00 in London; min is now, max is now + 90 days (GMT by then).
    expect(picker).toHaveValue('2026-09-30T09:00')
    expect(picker).toHaveAttribute('min', '2026-09-29T13:00')
    expect(picker).toHaveAttribute('max', '2026-12-28T12:00')
    expect(
      within(dialog).getByText(/Reads as 30 September 2026 at 09:00 \(BST\)\./),
    ).toBeInTheDocument()

    await events.click(
      within(dialog).getByRole('checkbox', { name: 'The exam board has approved these marks' }),
    )
    fireEvent.change(picker, { target: { value: '2027-01-15T09:00' } })
    await events.click(
      within(dialog).getByRole('button', {
        name: 'Schedule 1 module for 15 January 2027 at 09:00 (GMT)',
      }),
    )
    expect(
      await within(dialog).findByText('Choose a date within the next 90 days.'),
    ).toBeInTheDocument()
    expect(mock.calls('POST', '/api/admin/results/publish')).toHaveLength(0)

    fireEvent.change(picker, { target: { value: '2026-10-05T09:00' } })
    await events.click(
      within(dialog).getByRole('button', {
        name: 'Schedule 1 module for 5 October 2026 at 09:00 (BST)',
      }),
    )
    expect(
      await screen.findByText(
        'Scheduled: 1 module, 100 marks. Students see them at 5 October 2026 at 09:00 (BST).',
      ),
    ).toBeInTheDocument()
    expect(mock.calls('POST', '/api/admin/results/publish')[0]?.body).toMatchObject({
      publishAt: '2026-10-05T08:00:00.000Z',
    })
  })

  describe('while a publication is scheduled', () => {
    const scheduled = makePublicationInfo({
      id: 'pub-scheduled',
      publishAt: '2026-10-05T08:00:00.000Z',
      state: 'scheduled',
      gradeCount: 100,
    })
    const live = makePublicationInfo({
      id: 'pub-live',
      publishAt: '2026-09-28T08:00:00.000Z',
      state: 'live',
      gradeCount: 80000,
    })
    const scheduledMock = () =>
      createAdminMock({
        results: [
          makeAdminResults({
            modules: [
              makeResultsModule('CS3001', {
                status: 'scheduled',
                entered: 100,
                missing: 0,
                total: 100,
                publishedAt: '2026-10-05T08:00:00.000Z',
              }),
            ],
            publications: [scheduled, live],
          }),
        ],
      })

    it('returns a module to draft with the scheduled consequence and a reason', async () => {
      const { events, mock } = render(scheduledMock())
      const table = await screen.findByRole('table', { name: /Submission progress/ })
      expect(
        within(table).getByText('In the publication scheduled for 5 October 2026 at 09:00 (BST):'),
      ).toBeInTheDocument()
      await events.click(within(table).getByRole('button', { name: 'Return CS3001 to draft' }))
      const dialog = await screen.findByRole('alertdialog', { name: 'Return CS3001 to draft?' })
      expect(dialog).toHaveTextContent(
        'Students have not seen these marks. CS3001 will be removed from the publication scheduled for 5 October 2026 at 09:00 (BST).',
      )
      const reason = within(dialog).getByLabelText(/^Reason/)
      await events.type(reason, 'too short')
      await events.click(within(dialog).getByRole('button', { name: 'Return to draft' }))
      expect(
        await within(dialog).findByText('Give a reason of at least 10 characters.'),
      ).toBeInTheDocument()
      await events.type(reason, ' but now long enough')
      await events.click(within(dialog).getByRole('button', { name: 'Return to draft' }))
      expect(
        await screen.findByText('CS3001 is back in draft and out of the scheduled publication.'),
      ).toBeInTheDocument()
      expect(
        mock.calls('POST', '/api/admin/results/modules/CS3001/return-to-draft')[0]?.body,
      ).toEqual({ reason: 'too short but now long enough', academicYear: '2026/27' })
    })

    it('cancels a scheduled publication after stating that no student will see the marks', async () => {
      const { events, mock } = render(scheduledMock())
      const history = await screen.findByRole('table', { name: 'Publication history' })
      const row = within(history).getByText('5 October 2026 at 09:00 (BST)').closest('tr')!
      expect(within(row).getByText('Scheduled')).toBeInTheDocument()
      expect(within(row).getByRole('button', { name: 'Reschedule' })).toBeInTheDocument()
      await events.click(within(row).getByRole('button', { name: 'Cancel' }))
      const dialog = await screen.findByRole('alertdialog')
      expect(dialog).toHaveTextContent('Marks go back to Submitted and no student will see them.')
      await events.click(within(dialog).getByRole('button', { name: 'Cancel publication' }))
      expect(
        await screen.findByText('Cancelled. 100 marks of Autumn 2026/27 are back to Submitted.'),
      ).toBeInTheDocument()
      expect(mock.calls('DELETE', '/api/admin/results/publications/pub-scheduled')).toHaveLength(1)
    })

    it('unpublishes a live publication with a reason', async () => {
      const { events, mock } = render(scheduledMock())
      const history = await screen.findByRole('table', { name: 'Publication history' })
      const row = within(history).getByText('28 September 2026 at 09:00 (BST)').closest('tr')!
      await events.click(within(row).getByRole('button', { name: 'Unpublish' }))
      const dialog = await screen.findByRole('alertdialog', {
        name: 'Unpublish Autumn 2026/27 results?',
      })
      expect(dialog).toHaveTextContent('Students stop seeing these 80,000 marks immediately.')
      await events.type(within(dialog).getByLabelText(/^Reason/), 'Exam board recalled the marks')
      await events.click(within(dialog).getByRole('button', { name: 'Unpublish' }))
      expect(await screen.findByText(/Unpublished\. 80,000 marks/)).toBeInTheDocument()
      expect(
        mock.calls('POST', '/api/admin/results/publications/pub-live/unpublish')[0]?.body,
      ).toEqual({ reason: 'Exam board recalled the marks' })
    })

    it('reschedules within 90 days', async () => {
      const { events, mock } = render(scheduledMock())
      const history = await screen.findByRole('table', { name: 'Publication history' })
      await events.click(within(history).getByRole('button', { name: 'Reschedule' }))
      const dialog = await screen.findByRole('dialog', {
        name: /Reschedule Autumn 2026\/27 results/,
      })
      const picker = within(dialog).getByLabelText(/^Students see the marks at \(Europe\/London\)/)
      expect(picker).toHaveValue('2026-10-05T09:00')
      fireEvent.change(picker, { target: { value: '2026-10-06T10:30' } })
      await events.click(within(dialog).getByRole('button', { name: 'Reschedule' }))
      expect(
        await screen.findByText(
          'Autumn 2026/27 results now publish 6 October 2026 at 10:30 (BST).',
        ),
      ).toBeInTheDocument()
      expect(mock.calls('PUT', '/api/admin/results/publications/pub-scheduled')[0]?.body).toEqual({
        publishAt: '2026-10-06T09:30:00.000Z',
      })
    })
  })

  it('disables publishing when nothing is ready and says why', async () => {
    const mock = createAdminMock({
      results: [
        makeAdminResults({
          modules: [
            makeResultsModule('MA1001', { status: 'draft', entered: 0, missing: 40, total: 40 }),
          ],
        }),
      ],
    })
    render(mock)
    // Said once, in the card's description (it used to be repeated in a second paragraph).
    expect(
      await screen.findAllByText(
        'Nothing submitted for Autumn 2026/27 yet; lecturers submit modules from their Marks page.',
      ),
    ).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Publish results' })).toBeDisabled()
  })
})
