import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { makeAnnouncement, makeLecturerMe, makeStudentMe } from '@/test/factories'
import { problem } from '@/test/http'
import { renderRoutes } from '@/test/render'

import { sharedRoutes } from './routes'

describe('AnnouncementsPage', () => {
  it('lists announcements with pinned and scope badges and the body as written', async () => {
    renderRoutes(sharedRoutes, {
      route: '/announcements',
      user: makeStudentMe(),
      handlers: [
        http.get('/api/announcements', () =>
          HttpResponse.json([
            makeAnnouncement({ pinned: true, title: 'Autumn 2025/26 results are available' }),
            makeAnnouncement({
              scope: 'module',
              moduleCode: 'CS3001',
              title: 'Lab moved',
              body: 'Room B-201.\nBring a laptop.',
              author: 'Dr Grace Hopper',
            }),
          ]),
        ),
      ],
    })

    const pinned = await screen.findByRole('article', {
      name: 'Autumn 2025/26 results are available',
    })
    expect(within(pinned).getByText('Pinned')).toBeInTheDocument()
    expect(within(pinned).getByText('University')).toBeInTheDocument()
    expect(within(pinned).getByText('28 September 2026')).toHaveAttribute(
      'datetime',
      '2026-09-28T09:00:00Z',
    )

    const moduleNotice = screen.getByRole('article', { name: 'Lab moved' })
    expect(within(moduleNotice).getByText('CS3001')).toBeInTheDocument()
    expect(within(moduleNotice).getByText(/Room B-201\./)).toHaveClass('whitespace-pre-line')
    expect(within(moduleNotice).getByText('Posted by Dr Grace Hopper')).toBeInTheDocument()
  })

  it('says when there is nothing yet', async () => {
    renderRoutes(sharedRoutes, {
      route: '/announcements',
      handlers: [http.get('/api/announcements', () => HttpResponse.json([]))],
    })
    expect(await screen.findByText('No announcements yet.')).toBeInTheDocument()
  })

  it('offers Retry when the server is busy', async () => {
    renderRoutes(sharedRoutes, {
      route: '/announcements',
      handlers: [http.get('/api/announcements', () => problem('server-busy'))],
    })
    expect(
      await screen.findByText('The portal is very busy right now. Try again in a moment.'),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Try again/ })).toBeInTheDocument()
  })
})

describe('ForbiddenPage and NotFoundPage', () => {
  it('says who the page is for and offers the way home', async () => {
    renderRoutes(sharedRoutes, { route: '/forbidden', user: makeLecturerMe() })
    expect(
      await screen.findByRole('heading', { name: "You don't have access to that page", level: 1 }),
    ).toBeInTheDocument()
    expect(screen.getByText(/You're signed in as a lecturer\./)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to your home page' })).toHaveAttribute(
      'href',
      '/lecturer',
    )
  })

  it('offers Sign in to a visitor', async () => {
    renderRoutes(sharedRoutes, { route: '/no/such/page', user: null })
    expect(
      await screen.findByRole('heading', { name: "That page doesn't exist" }),
    ).toBeInTheDocument()
    expect(within(screen.getByRole('main')).getByRole('link', { name: 'Sign in' })).toHaveAttribute(
      'href',
      '/login',
    )
  })
})

describe('AccessibilityPage', () => {
  it('states the target, the testing, the limitations, the contact route and the date', async () => {
    renderRoutes(sharedRoutes, { route: '/accessibility', user: null })
    expect(
      await screen.findByRole('heading', { name: 'Accessibility statement', level: 1 }),
    ).toBeInTheDocument()
    for (const heading of [
      'Conformance target',
      'How we test',
      'Known limitations',
      'Reporting a problem',
      'About this statement',
    ]) {
      expect(screen.getByRole('heading', { name: heading, level: 2 })).toBeInTheDocument()
    }
    expect(screen.getByText(/WCAG\) 2.2 at level AA/)).toBeInTheDocument()
    expect(screen.getByText(/prepared on 29 September 2026/)).toBeInTheDocument()
  })
})
