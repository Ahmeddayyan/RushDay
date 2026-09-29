import { configure, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'

import { makeAdminMe, makeAuditEvent } from '@/test/factories'
import { createAdminMock } from '@/test/handlers/admin'
import { renderRoutes } from '@/test/render'

import { adminRoutes } from './routes'

// Admin pages render large lazy route trees; under coverage on a CI runner the first render
// can take longer than the default second.
configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 30_000 })

beforeAll(async () => {
  await import('./AuditLogPage')
})

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-09-29T12:00:00.000Z'))
})

afterEach(() => {
  vi.useRealTimers()
})

function render(route = '/admin/audit', mock = createAdminMock()) {
  const result = renderRoutes(adminRoutes, { route, user: makeAdminMe(), handlers: mock.handlers })
  return { ...result, mock }
}

const lastAuditQuery = (mock: ReturnType<typeof createAdminMock>) =>
  mock.calls('GET', '/api/admin/audit').at(-1)?.search

describe('AuditLogPage', () => {
  it('sends the filters as query parameters and keeps them in the URL', async () => {
    const { events, router, mock } = render()
    await screen.findByRole('table', { name: 'Audit log' })

    await events.selectOptions(screen.getByLabelText('Action'), 'grade.corrected')
    await events.type(screen.getByLabelText('Actor'), 'registry.admin{Enter}')
    await events.type(screen.getByLabelText('Student number'), 's000001{Enter}')
    await events.type(screen.getByLabelText('Module code'), 'cs3099{Enter}')
    await events.selectOptions(screen.getByLabelText('Date range'), '7d')

    await waitFor(() => {
      const search = lastAuditQuery(mock)
      expect(search?.get('action')).toBe('grade.corrected')
      expect(search?.get('actor')).toBe('registry.admin')
      expect(search?.get('studentNumber')).toBe('S000001')
      expect(search?.get('moduleCode')).toBe('CS3099')
      // Seven days of the zone's calendar, midnight to midnight in London (BST).
      expect(search?.get('from')).toBe('2026-09-22T23:00:00.000Z')
      expect(search?.get('to')).toBe('2026-09-29T23:00:00.000Z')
      expect(search?.get('pageSize')).toBe('50')
    })
    const url = new URLSearchParams(router.state.location.search)
    expect(url.get('action')).toBe('grade.corrected')
    expect(url.get('studentNumber')).toBe('S000001')
    expect(url.get('range')).toBe('7d')
  })

  it('reads its filters from the URL on arrival', async () => {
    const { mock } = render('/admin/audit?actor=admin&moduleCode=CS3001&page=2')
    await waitFor(() => {
      const search = lastAuditQuery(mock)
      expect(search?.get('actor')).toBe('admin')
      expect(search?.get('moduleCode')).toBe('CS3001')
      expect(search?.get('page')).toBe('2')
    })
    expect(screen.getByLabelText('Actor')).toHaveValue('admin')
  })

  it('shows details as text, so a stored script tag is only words', async () => {
    const mock = createAdminMock({
      audit: [
        makeAuditEvent({
          action: 'student.left',
          subjectType: 'Student',
          studentNumber: 'S000002',
          details: {
            reason: '<script>alert("x")</script>',
            before: { mark: 50, outcome: 'mark' },
            codes: ['CS3001', 'CS3099'],
            override: true,
          },
        }),
      ],
    })
    const { events } = render('/admin/audit', mock)
    const table = await screen.findByRole('table', { name: 'Audit log' })
    expect(within(table).getByText('Student marked as left')).toBeInTheDocument()
    const toggle = within(table).getByRole('button', { name: /Details/ })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    await events.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')

    expect(within(table).getByText('<script>alert("x")</script>')).toBeInTheDocument()
    expect(document.querySelector('main script')).toBeNull()
    expect(within(table).getByText('reason').tagName).toBe('DT')
    expect(within(table).getByText('CS3001, CS3099')).toBeInTheDocument()
    expect(within(table).getByText('yes')).toBeInTheDocument()
    expect(within(table).getByText('50')).toBeInTheDocument()
  })

  it('names a UUID subject by its username or a short id, keeping the full id in the details', async () => {
    const accountId = '01a0ece5-2a62-7a28-9a3a-da97b77d1336'
    const gradeId = '01a0ecef-aa1f-7ee6-8d55-e1992a4818bc'
    const mock = createAdminMock({
      audit: [
        makeAuditEvent({
          id: 'e1',
          action: 'account.provisioned',
          subjectType: 'Account',
          subjectId: accountId,
          details: { username: 'S981422' },
        }),
        makeAuditEvent({
          id: 'e2',
          action: 'grade.corrected',
          subjectType: 'Grade',
          subjectId: gradeId,
          details: { reason: 'Moderation' },
        }),
      ],
    })
    const { events } = render('/admin/audit', mock)
    const table = await screen.findByRole('table', { name: 'Audit log' })
    expect(within(table).getByText('Account S981422')).toBeInTheDocument()
    expect(within(table).getByText('Grade 01a0ecef…')).toBeInTheDocument()
    expect(within(table).queryByText(`Grade ${gradeId}`)).not.toBeInTheDocument()
    await events.click(within(table).getByRole('button', { name: /Details\s*of Mark corrected/ }))
    expect(within(table).getByText(gradeId)).toBeInTheDocument()
  })

  it('downloads the CSV with the same filters and says when it was truncated', async () => {
    Object.defineProperty(URL, 'createObjectURL', {
      configurable: true,
      value: vi.fn(() => 'blob:csv'),
    })
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    const mock = createAdminMock({ auditTruncatedAt: 50000 })
    const { events } = render('/admin/audit?actor=registry.admin', mock)
    await screen.findByRole('table', { name: 'Audit log' })

    await events.click(
      screen.getByRole('button', { name: 'Export CSV (up to 50,000 rows), downloads a file' }),
    )
    expect(
      await screen.findByText(
        'The export stopped at 50,000 rows. Narrow the date range to 31 days or less to get everything.',
      ),
    ).toBeInTheDocument()
    expect(click).toHaveBeenCalledTimes(1)
    const exported = mock.calls('GET', '/api/admin/audit/export.csv')[0]?.search
    expect(exported?.get('actor')).toBe('registry.admin')
    expect(exported?.get('page')).toBeNull()
  })

  it('says when nothing matches', async () => {
    render('/admin/audit?action=ops.reconciled')
    expect(await screen.findByText('Nothing recorded for these filters.')).toBeInTheDocument()
  })
})
