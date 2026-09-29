import { Outlet, type RouteObject } from 'react-router'
import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'

import { makeLecturerMe } from '@/test/factories'
import { buildLecturerModule, lecturerHandlers, resetLecturerStore } from '@/test/handlers/lecturer'
import { renderRoutes } from '@/test/render'

import type { ModuleOutletContext } from './ModulePage'
import { Component as RosterTab } from './RosterTab'

afterEach(() => {
  resetLecturerStore()
})

/** Stands in for `ModulePage`: supplies the outlet context `RosterTab` reads via `useModuleContext`. */
function ModuleFrame({ context }: { context: ModuleOutletContext }) {
  return <Outlet context={context} />
}

function routesFor(code: string, context: ModuleOutletContext): RouteObject[] {
  return [
    {
      path: `/lecturer/modules/${code}`,
      element: <ModuleFrame context={context} />,
      children: [{ index: true, element: <RosterTab /> }],
    },
  ]
}

describe('RosterTab', () => {
  it('lists the roster, paged and searched', async () => {
    const fixture = buildLecturerModule({ code: 'CS3001', rowCount: 3 })
    const routes = routesFor('CS3001', { module: fixture.summary, timeZone: 'Europe/London' })

    const { events } = renderRoutes(routes, {
      route: '/lecturer/modules/CS3001',
      user: makeLecturerMe(),
      handlers: lecturerHandlers,
    })

    expect(await screen.findByText('S000001')).toBeInTheDocument()
    expect(screen.getByText('Student 1')).toBeInTheDocument()
    expect(screen.getAllByText('Active')).toHaveLength(3)

    await events.type(screen.getByLabelText('Search students'), 'Student 2')
    // "Student 2" also matches the unfiltered page (keepPreviousData) until the debounced (250 ms)
    // search resolves; wait for the row the query should exclude to actually disappear.
    await waitFor(() => expect(screen.queryByText('S000001')).not.toBeInTheDocument())
    expect(screen.getByText('S000002')).toBeInTheDocument()
  })

  it("shows an empty state naming the module when it has no students", async () => {
    const fixture = buildLecturerModule({ code: 'CS3001', rowCount: 0, roster: [] })
    const routes = routesFor('CS3001', { module: fixture.summary, timeZone: undefined })

    renderRoutes(routes, {
      route: '/lecturer/modules/CS3001',
      user: makeLecturerMe(),
      handlers: lecturerHandlers,
    })

    expect(
      await screen.findByText('No students are enrolled on CS3001 this year.'),
    ).toBeInTheDocument()
  })
})
