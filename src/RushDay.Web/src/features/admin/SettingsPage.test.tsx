import { configure, screen, waitFor, within } from '@testing-library/react'
import { beforeAll, describe, expect, it, vi } from 'vitest'

import { makeAdminMe } from '@/test/factories'
import { createAdminMock } from '@/test/handlers/admin'
import { renderRoutes } from '@/test/render'

import { adminRoutes } from './routes'

// Admin pages render large lazy route trees; under coverage on a CI runner the first render
// can take longer than the default second.
configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 30_000 })

const WARNING =
  "Changing the year starts a new year for everyone: this year's enrolment windows no longer apply, credit budgets and module places start from zero, and last year's modules become 'completed'. Create the windows for 2027/28 first."

beforeAll(async () => {
  await import('./SettingsPage')
})

function render(mock = createAdminMock()) {
  const result = renderRoutes(adminRoutes, {
    route: '/admin/settings',
    user: makeAdminMe(),
    handlers: mock.handlers,
  })
  return { ...result, mock }
}

describe('SettingsPage', () => {
  it('warns what a year change does and asks for confirmation before saving it', async () => {
    const { events, mock } = render()
    const year = await screen.findByLabelText(/^Academic year/)
    expect(year).toHaveValue('2026/27')
    expect(screen.queryByText(WARNING)).not.toBeInTheDocument()

    await events.clear(year)
    await events.type(year, '2027/28')
    expect(screen.getByText(WARNING)).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Save settings' }))
    const dialog = await screen.findByRole('alertdialog', {
      name: 'Change the academic year to 2027/28?',
    })
    expect(dialog).toHaveTextContent(WARNING)

    // Cancel: nothing is sent.
    await events.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    expect(mock.calls('PUT', '/api/admin/settings')).toHaveLength(0)

    await events.click(screen.getByRole('button', { name: 'Save settings' }))
    const again = await screen.findByRole('alertdialog')
    await events.click(within(again).getByRole('button', { name: 'Start 2027/28' }))
    expect(
      await screen.findByText('Settings saved. The academic year is now 2027/28.'),
    ).toBeInTheDocument()
    expect(mock.calls('PUT', '/api/admin/settings')[0]?.body).toEqual({
      academicYear: '2027/28',
      currentSemester: 'autumn',
      institutionName: 'Northbridge University',
      institutionShortName: 'Northbridge',
      timeZone: 'Europe/London',
      supportEmail: null,
      supportUrl: null,
    })
  })

  it('saves other changes without the year confirmation', async () => {
    const { events, mock } = render()
    const email = await screen.findByLabelText(/^Academic office email/)
    expect(
      screen.getAllByText('Shown wherever students are told to contact the academic office.'),
    ).toHaveLength(2)
    await events.type(email, 'registry@northbridge.ac.uk')
    await events.selectOptions(screen.getByLabelText(/^Current semester/), 'spring')
    await events.click(screen.getByRole('button', { name: 'Save settings' }))
    expect(await screen.findByText('Settings saved.')).toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(mock.calls('PUT', '/api/admin/settings')[0]?.body).toMatchObject({
      currentSemester: 'spring',
      supportEmail: 'registry@northbridge.ac.uk',
    })
  })

  it('checks the year, the zone and the help address in the browser', async () => {
    const { events, mock } = render()
    const year = await screen.findByLabelText(/^Academic year/)
    await events.clear(year)
    await events.type(year, '2027/29')
    const zone = screen.getByLabelText(/^Time zone/)
    await events.clear(zone)
    await events.type(zone, 'London')
    await events.type(screen.getByLabelText(/^Academic office help URL/), 'http://help.example')
    await events.click(screen.getByRole('button', { name: 'Save settings' }))
    expect(await screen.findByText('Write the academic year like 2026/27.')).toBeInTheDocument()
    expect(
      screen.getByText('Enter an IANA time zone id, for example Europe/London.'),
    ).toBeInTheDocument()
    expect(screen.getByText('Enter a full https:// address.')).toBeInTheDocument()
    expect(mock.calls('PUT', '/api/admin/settings')).toHaveLength(0)
  })
})
