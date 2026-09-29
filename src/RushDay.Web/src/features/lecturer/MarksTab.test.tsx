import { Outlet, type RouteObject } from 'react-router'
import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'

import { makeLecturerMe } from '@/test/factories'
import { buildLecturerModule, lecturerHandlers, makeMarksRowFixture, resetLecturerStore } from '@/test/handlers/lecturer'
import { renderRoutes } from '@/test/render'

import { Component as MarksTab } from './MarksTab'
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
      path: `/lecturer/modules/${code}/marks`,
      element: <ModuleFrame context={context} />,
      children: [{ index: true, element: <MarksTab /> }],
    },
  ]
}

describe('MarksTab', () => {
  it('lets the leader submit once every active student has an outcome, and shows the locked banner after', async () => {
    const fixture = buildLecturerModule({
      code: 'CS3001',
      rowCount: 2,
      myRole: 'leader',
      rows: [
        makeMarksRowFixture(0, { outcome: 'mark', mark: 70, gradeStatus: 'draft', version: 1 }),
        makeMarksRowFixture(1, { outcome: 'absent', mark: null, gradeStatus: 'draft', version: 1 }),
      ],
    })
    const routes = routesFor('CS3001', { module: fixture.summary, timeZone: 'Europe/London' })
    const { events } = renderRoutes(routes, {
      route: '/lecturer/modules/CS3001/marks',
      user: makeLecturerMe(),
      handlers: lecturerHandlers,
    })

    await events.click(await screen.findByRole('button', { name: 'Submit module' }))
    await waitFor(() => expect(screen.queryByText(/no mark yet/)).not.toBeInTheDocument())
    const confirmButtons = screen.getAllByRole('button', { name: 'Submit module' })
    await events.click(confirmButtons[confirmButtons.length - 1]!)

    expect(
      await screen.findByText(/^Submitted on .*Marks are locked\./),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit module' })).not.toBeInTheDocument()
    expect(await screen.findByLabelText('Mark for Student 1')).toBeDisabled()
  })

  it('shows a teacher only the caption, never the submit control', async () => {
    const fixture = buildLecturerModule({ code: 'CS3001', rowCount: 2, myRole: 'teacher' })
    const routes = routesFor('CS3001', { module: fixture.summary, timeZone: undefined })
    renderRoutes(routes, {
      route: '/lecturer/modules/CS3001/marks',
      user: makeLecturerMe(),
      handlers: lecturerHandlers,
    })

    expect(await screen.findByText('Ask Dr Grace Hopper to submit.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit module' })).not.toBeInTheDocument()
  })
})
