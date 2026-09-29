import type { RouteObject } from 'react-router'
import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'

import { makeLecturerMe } from '@/test/factories'
import { buildLecturerModule, lecturerHandlers, resetLecturerStore } from '@/test/handlers/lecturer'
import { renderRoutes } from '@/test/render'

import { Component as ModulePage } from './ModulePage'

afterEach(() => {
  resetLecturerStore()
})

const routes: RouteObject[] = [
  {
    path: '/lecturer/modules/:code',
    element: <ModulePage />,
    children: [{ index: true, element: <p>Roster placeholder</p> }],
  },
]

describe('ModulePage', () => {
  it('shows the module header, badges and tabs, and renders the matched tab', async () => {
    buildLecturerModule({ code: 'CS3001', title: 'Distributed Systems', myRole: 'leader' })

    renderRoutes(routes, {
      route: '/lecturer/modules/CS3001',
      user: makeLecturerMe(),
      handlers: lecturerHandlers,
    })

    expect(await screen.findByRole('heading', { name: 'CS3001 · Distributed Systems' })).toBeInTheDocument()
    expect(screen.getByText('15 credits')).toBeInTheDocument()
    expect(screen.getByText('leader')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Marks' })).toHaveAttribute(
      'href',
      '/lecturer/modules/CS3001/marks',
    )
    expect(screen.getByText('Roster placeholder')).toBeInTheDocument()
  })

  it("shows an error instead of a redirect loop for a module that isn't assigned", async () => {
    buildLecturerModule({ code: 'CS3001' })

    renderRoutes(routes, {
      route: '/lecturer/modules/CS9999',
      user: makeLecturerMe(),
      handlers: lecturerHandlers,
    })

    expect(await screen.findByText("This module isn't assigned to you.")).toBeInTheDocument()
  })
})
