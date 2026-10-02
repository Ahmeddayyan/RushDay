import { configure, screen, waitFor, within } from '@testing-library/react'
import { delay, http } from 'msw'
import { beforeAll, describe, expect, it, vi } from 'vitest'

import { makeAccountView, makeAdminMe, makeDemoStatus } from '@/test/factories'
import { createAdminMock, makeAdminStudentRow } from '@/test/handlers/admin'
import { statusHandler } from '@/test/handlers/public'
import { renderRoutes } from '@/test/render'

import { adminRoutes } from './routes'

// Admin pages render large lazy route trees; under coverage on a CI runner the first render
// can take longer than the default second.
configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 30_000 })

const admin = makeAdminMe({ id: 'admin-self', username: 'registry.admin' })

// Load the lazy page once, so the first test times the page and not the module transform.
beforeAll(async () => {
  await import('./AccountsPage')
})

function render(mock = createAdminMock(), extraHandlers: Parameters<typeof renderRoutes>[1] = {}) {
  const result = renderRoutes(adminRoutes, {
    route: '/admin/accounts',
    user: admin,
    ...extraHandlers,
    handlers: [...(extraHandlers.handlers ?? []), ...mock.handlers],
  })
  return { ...result, mock }
}

describe('AccountsPage', () => {
  it('keeps the current page on screen while the next one loads', async () => {
    const mock = createAdminMock({
      accounts: Array.from({ length: 30 }, (_, index) =>
        makeAccountView({
          id: `id-${index}`,
          username: `user${String(index + 1).padStart(2, '0')}`,
          displayName: `Person ${index + 1}`,
        }),
      ),
    })
    const slowSecondPage = http.get('/api/admin/accounts', async ({ request }) => {
      if (new URL(request.url).searchParams.get('page') === '2') await delay(200)
    })
    const { events, router } = render(mock, { handlers: [slowSecondPage] })

    expect(await screen.findByText('user01')).toBeInTheDocument()
    expect(screen.getByText('Showing 1–25 of 30 accounts')).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Next page' }))
    // The first page stays while the second loads, and paging is paused.
    expect(screen.getByText('user01')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled()
    expect(router.state.location.search).toBe('?page=2')

    expect(await screen.findByText('user26')).toBeInTheDocument()
    expect(screen.queryByText('user01')).not.toBeInTheDocument()
    expect(screen.getByText('Showing 26–30 of 30 accounts')).toBeInTheDocument()
    expect(mock.calls('GET', '/api/admin/accounts').at(-1)?.search.get('pageSize')).toBe('25')
  })

  it('provisions an account and shows the temporary password exactly once', async () => {
    const mock = createAdminMock({
      students: [
        makeAdminStudentRow({
          studentNumber: 'S000003',
          fullName: 'Chloe Diaz',
          email: null,
          accountState: 'none',
        }),
      ],
    })
    const { events } = render(mock)
    await screen.findByRole('button', { name: 'Actions for S000002' })

    await events.click(screen.getByRole('button', { name: 'Provision account' }))
    const dialog = await screen.findByRole('dialog', { name: 'Provision account' })
    expect(within(dialog).getByLabelText(/^Role/)).toHaveValue('Student')

    await events.type(within(dialog).getByRole('combobox', { name: /^Student/ }), 'S00000')
    await events.click(await within(dialog).findByRole('option', { name: /S000003 Chloe Diaz/ }))
    expect(within(dialog).getByLabelText(/^Username/)).toHaveValue('S000003')
    expect(within(dialog).getByLabelText(/^Display name/)).toHaveValue('Chloe Diaz')

    await events.click(within(dialog).getByRole('button', { name: 'Provision account' }))

    const shown = await screen.findByRole('dialog', { name: 'Temporary password for S000003' })
    expect(within(shown).getByText('Kx7mPq2RtW9vNz4H')).toBeInTheDocument()
    expect(
      within(shown).getByText(
        "This won't be shown again. The person must change it and, for administrators, set up two-step verification at first sign-in.",
      ),
    ).toBeInTheDocument()
    expect(mock.calls('POST', '/api/admin/accounts')[0]?.body).toEqual({
      username: 'S000003',
      displayName: 'Chloe Diaz',
      role: 'Student',
      studentNumber: 'S000003',
      staffNumber: null,
      email: null,
    })

    await events.click(within(shown).getByRole('button', { name: 'Done' }))
    await waitFor(() => expect(screen.queryByText('Kx7mPq2RtW9vNz4H')).not.toBeInTheDocument())
    expect(await screen.findByRole('button', { name: 'Actions for S000003' })).toBeInTheDocument()
  })

  it('explains an empty student list and offers to create a student', async () => {
    const mock = createAdminMock({ students: [] })
    const { events } = render(mock)
    await screen.findByRole('button', { name: 'Actions for S000002' })
    await events.click(screen.getByRole('button', { name: 'Provision account' }))
    const dialog = await screen.findByRole('dialog', { name: 'Provision account' })

    await events.click(within(dialog).getByRole('combobox', { name: /^Student/ }))
    expect(
      await within(dialog).findByText(
        'Every student already has an account. Create a student first, or use Reset password on an existing one.',
      ),
    ).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Create student' })).toBeInTheDocument()
    const lookup = mock.calls('GET', '/api/admin/students').at(-1)
    expect(lookup?.search.get('accountState')).toBe('none')
  })

  it('keeps demo accounts read-only', async () => {
    const { events } = render()
    const demoActions = await screen.findByRole('button', { name: 'Actions for S000001' })
    expect(demoActions).toHaveAttribute('aria-disabled', 'true')
    expect(demoActions).toHaveAccessibleDescription('Demo accounts are read-only')
    await events.click(demoActions)
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Actions for S000002' }))
    const menu = await screen.findByRole('menu')
    expect(within(menu).getByRole('menuitem', { name: /Reset password/ })).toBeInTheDocument()
    expect(within(menu).getByRole('menuitem', { name: /^Lock/ })).toBeInTheDocument()
    expect(within(menu).getByRole('menuitem', { name: /^Disable/ })).toBeInTheDocument()
  })

  it('names who locked an account and until when', async () => {
    const mock = createAdminMock({
      accounts: [
        makeAccountView({
          id: 'a',
          username: 'locked.by.admin',
          state: 'locked',
          lockoutEnd: '9999-12-31T00:00:00.000Z',
        }),
        makeAccountView({
          id: 'b',
          username: 'locked.out',
          state: 'locked',
          lockoutEnd: '2026-09-29T11:15:00.000Z',
          mustChangePassword: true,
          mfaEnabled: true,
        }),
        makeAccountView({ id: 'c', username: 'gone', state: 'disabled' }),
      ],
    })
    render(mock)
    expect(await screen.findByText('Locked by administrator')).toBeInTheDocument()
    expect(screen.getByText('Locked until 29 September 2026 at 12:15 (BST)')).toBeInTheDocument()
    expect(screen.getByText('Disabled', { selector: 'span' })).toBeInTheDocument()
    expect(screen.getByText('Must change password')).toBeInTheDocument()
    expect(screen.getByText('Two-step on')).toBeInTheDocument()
  })

  it('locks an account after confirming the consequence', async () => {
    const mock = createAdminMock()
    const { events } = render(mock)
    await events.click(await screen.findByRole('button', { name: 'Actions for S000002' }))
    await events.click(await screen.findByRole('menuitem', { name: /^Lock/ }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Lock S000002?' })
    expect(dialog).toHaveTextContent(/every session ends/)
    await events.click(within(dialog).getByRole('button', { name: 'Lock account' }))
    expect(await screen.findByText('Locked by administrator')).toBeInTheDocument()
    expect(
      mock.calls('POST', `/api/admin/accounts/${mock.state.accounts[0]!.id}/lock`),
    ).toHaveLength(1)
  })

  it('warns before an administrator resets their own password', async () => {
    const mock = createAdminMock({
      accounts: [
        makeAccountView({
          id: 'admin-self',
          username: 'registry.admin',
          role: 'Admin',
          studentNumber: null,
        }),
      ],
    })
    const { events } = render(mock)
    await events.click(await screen.findByRole('button', { name: 'Actions for registry.admin' }))
    await events.click(await screen.findByRole('menuitem', { name: /Reset password/ }))
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog).toHaveTextContent("You'll be signed out after copying the temporary password.")
  })

  it('filters by search, role and state in the URL, and shows the demo banner', async () => {
    const { events, router, mock } = render(createAdminMock(), {
      handlers: [statusHandler(makeDemoStatus())],
    })
    expect(
      await screen.findByText(
        /Demo mode is on: 3 demo accounts use published passwords\. They are disabled automatically/,
      ),
    ).toBeInTheDocument()
    await events.selectOptions(screen.getByLabelText('Role'), 'Lecturer')
    await events.selectOptions(screen.getByLabelText('State'), 'locked')
    expect(await screen.findByText('No accounts match these filters.')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?role=Lecturer&state=locked')
    const last = mock.calls('GET', '/api/admin/accounts').at(-1)
    expect(last?.search.get('role')).toBe('Lecturer')
    expect(last?.search.get('state')).toBe('locked')
    await events.click(screen.getByRole('button', { name: 'Clear filters' }))
    expect(await screen.findByRole('button', { name: 'Actions for S000002' })).toBeInTheDocument()
  })
})
