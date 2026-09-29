import { Outlet, type RouteObject } from 'react-router'
import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'

import { makeLecturerMe } from '@/test/factories'
import { buildLecturerModule, lecturerHandlers, resetLecturerStore } from '@/test/handlers/lecturer'
import { renderRoutes } from '@/test/render'

import { Component as ModuleAnnouncementsTab } from './ModuleAnnouncementsTab'
import type { ModuleOutletContext } from './ModulePage'

afterEach(() => {
  resetLecturerStore()
})

function ModuleFrame({ context }: { context: ModuleOutletContext }) {
  return <Outlet context={context} />
}

function routesFor(code: string, context: ModuleOutletContext): RouteObject[] {
  return [
    {
      path: `/lecturer/modules/${code}/announcements`,
      element: <ModuleFrame context={context} />,
      children: [{ index: true, element: <ModuleAnnouncementsTab /> }],
    },
  ]
}

describe('ModuleAnnouncementsTab', () => {
  it('creates, edits and deletes a module announcement', async () => {
    const fixture = buildLecturerModule({ code: 'CS3001', rowCount: 1 })
    const routes = routesFor('CS3001', { module: fixture.summary, timeZone: 'Europe/London' })
    const { events } = renderRoutes(routes, {
      route: '/lecturer/modules/CS3001/announcements',
      user: makeLecturerMe(),
      handlers: lecturerHandlers,
    })

    expect(await screen.findByText('No announcements for this module yet.')).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'New announcement' }))
    await events.type(screen.getByLabelText(/^Title/), 'Room change')
    await events.type(screen.getByLabelText(/^Announcement text/), 'CS3001 moves to B-201 next week.')
    await events.click(screen.getByRole('button', { name: 'Post announcement' }))

    expect(await screen.findByText('Room change')).toBeInTheDocument()
    expect(screen.getByText('CS3001 moves to B-201 next week.')).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Edit' }))
    const titleField = screen.getByLabelText(/^Title/)
    await events.clear(titleField)
    await events.type(titleField, 'Room change confirmed')
    await events.click(screen.getByRole('button', { name: 'Save changes' }))

    expect(await screen.findByText('Room change confirmed')).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Delete' }))
    const deleteButtons = screen.getAllByRole('button', { name: 'Delete' })
    await events.click(deleteButtons[deleteButtons.length - 1]!)

    await waitFor(() =>
      expect(screen.getByText('No announcements for this module yet.')).toBeInTheDocument(),
    )
  }, 15_000)
})
