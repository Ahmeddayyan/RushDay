import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'

import { makeLecturerMe } from '@/test/factories'
import { buildLecturerModule, lecturerHandlers, resetLecturerStore } from '@/test/handlers/lecturer'
import { renderWithProviders } from '@/test/render'

import { Component as MyModulesPage } from './MyModulesPage'

afterEach(() => {
  resetLecturerStore()
})

describe('MyModulesPage', () => {
  it('lists every taught module with its marks status and role, sorted by code', async () => {
    buildLecturerModule({ code: 'CS3099', title: 'Software Engineering Project', myRole: 'teacher' })
    buildLecturerModule({ code: 'CS3001', title: 'Distributed Systems', myRole: 'leader' })

    renderWithProviders(<MyModulesPage />, { user: makeLecturerMe(), handlers: lecturerHandlers })

    const rows = await screen.findAllByRole('row')
    // Header row plus two module rows, CS3001 sorted before CS3099 by default.
    expect(rows).toHaveLength(3)
    expect(rows[1]).toHaveTextContent('CS3001')
    expect(rows[2]).toHaveTextContent('CS3099')
    expect(screen.getAllByText('leader')).toHaveLength(1)
    expect(screen.getAllByText('teacher')).toHaveLength(1)
  })

  it('reverses the sort when a column header is activated again', async () => {
    buildLecturerModule({ code: 'CS3001', title: 'Distributed Systems' })
    buildLecturerModule({ code: 'CS3099', title: 'Software Engineering Project' })
    const { events } = renderWithProviders(<MyModulesPage />, {
      user: makeLecturerMe(),
      handlers: lecturerHandlers,
    })

    await screen.findAllByRole('row')
    await events.click(screen.getByRole('button', { name: /Sort by Code/ }))
    const rows = screen.getAllByRole('row')
    expect(rows[1]).toHaveTextContent('CS3099')
    expect(rows[2]).toHaveTextContent('CS3001')
  })

  it('shows the empty state when no modules are assigned', async () => {
    renderWithProviders(<MyModulesPage />, { user: makeLecturerMe(), handlers: lecturerHandlers })
    expect(
      await screen.findByText('No modules are assigned to you. Ask an administrator.'),
    ).toBeInTheDocument()
  })
})
