import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'

import { makeAnnouncement, makeLecturerMe } from '@/test/factories'
import { buildLecturerModule, lecturerHandlers, makeMarksRowFixture, resetLecturerStore } from '@/test/handlers/lecturer'
import { renderWithProviders } from '@/test/render'
import { http, HttpResponse } from 'msw'

import { Component as LecturerHomePage } from './LecturerHomePage'

afterEach(() => {
  resetLecturerStore()
})

describe('LecturerHomePage', () => {
  it('summarises modules taught and grading progress, and lists announcements', async () => {
    buildLecturerModule({
      code: 'CS3001',
      title: 'Distributed Systems',
      rowCount: 2,
      rows: [
        makeMarksRowFixture(0, { outcome: 'mark', mark: 70, gradeStatus: 'draft', version: 1 }),
        makeMarksRowFixture(1, {}),
      ],
    })

    renderWithProviders(<LecturerHomePage />, {
      user: makeLecturerMe(),
      handlers: [
        ...lecturerHandlers,
        http.get('/api/announcements', () => HttpResponse.json([makeAnnouncement({ title: 'Room change' })])),
      ],
    })

    expect(await screen.findByText('Modules taught')).toBeInTheDocument()
    expect(await screen.findByText('CS3001')).toBeInTheDocument()
    expect(screen.getByText('1 of 2 entered, 1 missing')).toBeInTheDocument()
    expect(screen.getByText('Room change')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Enter marks' })).toHaveAttribute(
      'href',
      '/lecturer/modules/CS3001/marks',
    )
  })

  it('shows the empty state when no modules are assigned', async () => {
    renderWithProviders(<LecturerHomePage />, { user: makeLecturerMe(), handlers: lecturerHandlers })
    expect(await screen.findByText('No modules are assigned to you.')).toBeInTheDocument()
  })
})
