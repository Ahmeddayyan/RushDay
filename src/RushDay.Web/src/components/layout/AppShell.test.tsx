import { screen, waitFor, within } from '@testing-library/react'
import type { RouteObject } from 'react-router'
import { describe, expect, it } from 'vitest'

import { makeAdminMe, makeLecturerMe, makeStudentMe } from '@/test/factories'
import { statusHandler } from '@/test/handlers/public'
import { makePublicStatus } from '@/test/factories'
import { renderRoutes } from '@/test/render'

import { AppShell } from './AppShell'

function Page({ title }: { title: string }) {
  return (
    <h1 tabIndex={-1} className="outline-none">
      {title}
    </h1>
  )
}

const routes: RouteObject[] = [
  {
    element: <AppShell />,
    children: [
      { path: '/student', element: <Page title="Student home" /> },
      { path: '/student/results', element: <Page title="Results" /> },
      { path: '/announcements', element: <Page title="Announcements" /> },
      { path: '/lecturer', element: <Page title="Lecturer home" /> },
      { path: '/admin', element: <Page title="Overview" /> },
      { path: '/accessibility', element: <Page title="Accessibility statement" /> },
    ],
  },
  { path: '/login', element: <Page title="Sign in" /> },
]

describe('AppShell', () => {
  it('has the landmarks, the skip link and the student navigation with bottom tabs', async () => {
    renderRoutes(routes, { route: '/student', user: makeStudentMe() })
    expect(await screen.findByRole('heading', { name: 'Student home' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveAttribute(
      'href',
      '#main',
    )
    expect(screen.getByRole('banner')).toBeInTheDocument()
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main')
    expect(screen.getByRole('contentinfo')).toBeInTheDocument()

    const primary = screen.getByRole('navigation', { name: 'Primary' })
    expect(
      within(primary)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['Home', 'Results', 'Timetable', 'Modules', 'Announcements'])
    expect(within(primary).getByRole('link', { name: 'Home' })).toHaveAttribute(
      'aria-current',
      'page',
    )

    const tabs = screen.getByRole('navigation', { name: 'Shortcuts' })
    expect(within(tabs).getAllByRole('link')).toHaveLength(4)
  })

  it('gives lecturers and administrators their own navigation and no bottom tabs', async () => {
    renderRoutes(routes, { route: '/lecturer', user: makeLecturerMe() })
    const primary = await screen.findByRole('navigation', { name: 'Primary' })
    expect(within(primary).getByRole('link', { name: 'My modules' })).toHaveAttribute(
      'href',
      '/lecturer/modules',
    )
    expect(screen.queryByRole('navigation', { name: 'Shortcuts' })).not.toBeInTheDocument()
  })

  it('lists every administrator area', async () => {
    renderRoutes(routes, { route: '/admin', user: makeAdminMe() })
    const primary = await screen.findByRole('navigation', { name: 'Primary' })
    expect(within(primary).getAllByRole('link')).toHaveLength(11)
    expect(within(primary).getByRole('link', { name: 'Operations' })).toHaveAttribute(
      'href',
      '/admin/ops',
    )
  })

  it('moves focus to the new page heading after navigation', async () => {
    const { events } = renderRoutes(routes, { route: '/student', user: makeStudentMe() })
    const primary = await screen.findByRole('navigation', { name: 'Primary' })
    // The first render keeps focus where the browser put it.
    expect(screen.getByRole('heading', { name: 'Student home' })).not.toHaveFocus()
    await events.click(within(primary).getByRole('link', { name: 'Results' }))
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Results' })).toHaveFocus())
  })

  it('opens the navigation drawer as a dialog and closes it after a link', async () => {
    const { events } = renderRoutes(routes, { route: '/student', user: makeStudentMe() })
    await events.click(await screen.findByRole('button', { name: 'Open navigation' }))
    const drawer = await screen.findByRole('dialog', { name: 'Navigation' })
    expect(within(drawer).getByRole('radiogroup', { name: 'Theme' })).toBeInTheDocument()
    await events.click(within(drawer).getByRole('link', { name: 'Announcements' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByRole('heading', { name: 'Announcements' })).toBeInTheDocument()
  })

  it('shows visitors the wordmark, the theme toggle and Sign in, with no navigation', async () => {
    renderRoutes(routes, { route: '/accessibility', user: null })
    expect(
      await screen.findByRole('heading', { name: 'Accessibility statement' }),
    ).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Primary' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/login')
    expect(screen.getByRole('link', { name: 'RushDay home' })).toBeInTheDocument()
  })

  it('names the institution and the year in the sidebar and footer', async () => {
    renderRoutes(routes, {
      route: '/student',
      user: makeStudentMe(),
      handlers: [
        statusHandler(
          makePublicStatus({
            institution: {
              ...makePublicStatus().institution,
              privacyNoticeUrl: 'https://example.ac.uk/privacy',
            },
          }),
        ),
      ],
    })
    expect((await screen.findAllByText('Northbridge University')).length).toBeGreaterThanOrEqual(2)
    expect(screen.getByText('2026/27 · Autumn semester')).toBeInTheDocument()
    expect(
      within(screen.getByRole('contentinfo')).getByRole('link', { name: /Privacy notice/ }),
    ).toHaveAttribute('href', 'https://example.ac.uk/privacy')
  })

  it('opens the user menu with the role and signs out', async () => {
    const { events, router } = renderRoutes(routes, { route: '/student', user: makeStudentMe() })
    await events.click(await screen.findByRole('button', { name: /account menu/ }))
    const menu = await screen.findByRole('menu')
    expect(within(menu).getByText('Aisha Khan')).toBeInTheDocument()
    expect(within(menu).getByText('Student')).toBeInTheDocument()
    expect(within(menu).getByRole('menuitem', { name: 'Account' })).toHaveAttribute(
      'href',
      '/account',
    )
    await events.click(within(menu).getByRole('menuitem', { name: 'Sign out' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
  })
})
