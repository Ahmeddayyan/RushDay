import { screen, waitFor } from '@testing-library/react'
import { http } from 'msw'
import { useLocation, type RouteObject } from 'react-router'
import { describe, expect, it } from 'vitest'

import { makeAdminMe, makeLecturerMe, makeStudentMe } from '@/test/factories'
import { renderRoutes } from '@/test/render'

import { useAuth } from './AuthProvider'
import { gateRedirect } from './gates'
import { PublicOnly, RequireAuth, RequireRole, RootRedirect } from './guards'

function SignOutButton() {
  const { logout } = useAuth()
  return (
    <button type="button" onClick={() => void logout()}>
      Sign out
    </button>
  )
}

function Where({ name }: { name: string }) {
  const location = useLocation()
  return (
    <h1>
      {name} at {location.pathname}
      {location.search}
    </h1>
  )
}

const routes: RouteObject[] = [
  { path: '/', element: <RootRedirect /> },
  { element: <PublicOnly />, children: [{ path: '/login', element: <Where name="login" /> }] },
  {
    element: <RequireAuth />,
    children: [
      { path: '/account', element: <Where name="account" /> },
      { path: '/account/password', element: <Where name="password" /> },
      { path: '/account/mfa', element: <Where name="mfa" /> },
    ],
  },
  {
    element: <RequireRole roles={['Student']} />,
    children: [{ path: '/student/*', element: <Where name="student" /> }],
  },
  {
    element: <RequireRole roles={['Lecturer']} />,
    children: [{ path: '/lecturer', element: <Where name="lecturer" /> }],
  },
  {
    element: <RequireRole roles={['Admin']} />,
    children: [{ path: '/admin/*', element: <Where name="admin" /> }],
  },
  { path: '/forbidden', element: <Where name="forbidden" /> },
]

async function landsOn(text: string) {
  expect(await screen.findByRole('heading', { name: text })).toBeInTheDocument()
}

describe('guards', () => {
  it('shows the splash while the session check is in flight', () => {
    renderRoutes(routes, {
      route: '/student',
      boot: true,
      handlers: [http.get('/api/auth/me', () => new Promise<Response>(() => {}))],
    })
    expect(screen.getByText('Loading RushDay')).toBeInTheDocument()
  })

  it('sends an anonymous visitor to /login with returnTo', async () => {
    const { router } = renderRoutes(routes, { route: '/student/results?year=2025', user: null })
    await landsOn('login at /login?returnTo=%2Fstudent%2Fresults%3Fyear%3D2025')
    expect(router.state.location.search).toBe('?returnTo=%2Fstudent%2Fresults%3Fyear%3D2025')
  })

  it('also guards through a real boot', async () => {
    renderRoutes(routes, { route: '/account', user: null, boot: true })
    await landsOn('login at /login?returnTo=%2Faccount')
  })

  it('sends a signed-in user with the wrong role to /forbidden, with who the page is for', async () => {
    const { router } = renderRoutes(routes, { route: '/admin/accounts', user: makeStudentMe() })
    await landsOn('forbidden at /forbidden')
    expect(router.state.location.state).toEqual({ from: '/admin/accounts', roles: ['Admin'] })
  })

  it('lets the right role through', async () => {
    renderRoutes(routes, { route: '/lecturer', user: makeLecturerMe() })
    await landsOn('lecturer at /lecturer')
  })

  it('sends a user who must change their password to the password page first, keeping the destination', async () => {
    renderRoutes(routes, {
      route: '/admin/results',
      user: makeAdminMe({ mustChangePassword: true, mfaSetupRequired: true, mfaEnabled: false }),
    })
    await landsOn('password at /account/password?required=1&returnTo=%2Fadmin%2Fresults')
  })

  it('applies the MFA gate after the password gate', async () => {
    renderRoutes(routes, {
      route: '/admin',
      user: makeAdminMe({ mustChangePassword: false, mfaSetupRequired: true, mfaEnabled: false }),
    })
    await landsOn('mfa at /account/mfa?required=1&returnTo=%2Fadmin')
  })

  it('moves a user from the password page to the MFA gate once the password is done, carrying returnTo', async () => {
    renderRoutes(routes, {
      route: '/account/password?required=1&returnTo=%2Fadmin%2Faudit',
      user: makeAdminMe({ mfaSetupRequired: true, mfaEnabled: false }),
    })
    await landsOn('mfa at /account/mfa?required=1&returnTo=%2Fadmin%2Faudit')
  })

  it('keeps a user on the password page while the password gate applies, even from the MFA page', async () => {
    renderRoutes(routes, {
      route: '/account/mfa?required=1',
      user: makeAdminMe({ mustChangePassword: true, mfaSetupRequired: true }),
    })
    await landsOn('password at /account/password?required=1')
  })

  it('sends a signed-in visitor away from /login to a safe returnTo', async () => {
    renderRoutes(routes, { route: '/login?returnTo=%2Faccount', user: makeStudentMe() })
    await landsOn('account at /account')
  })

  it.each(['//evil', '/\\evil.com', 'https://evil.example', '/login'])(
    'ignores the unsafe returnTo %s and goes to the role home',
    async (returnTo) => {
      renderRoutes(routes, {
        route: `/login?returnTo=${encodeURIComponent(returnTo)}`,
        user: makeStudentMe(),
      })
      await landsOn('student at /student')
    },
  )

  it('RootRedirect sends each role home and visitors to /login', async () => {
    renderRoutes(routes, { route: '/', user: makeAdminMe() })
    await landsOn('admin at /admin')
  })

  it('applies the gates straight after sign-in, before the destination route is involved', async () => {
    renderRoutes(routes, {
      route: '/login?returnTo=%2Fstudent%2Fresults',
      user: makeStudentMe({ mustChangePassword: true }),
    })
    await landsOn('password at /account/password?required=1&returnTo=%2Fstudent%2Fresults')
  })

  it('RootRedirect sends a gated administrator to the MFA gate with the role home as returnTo', async () => {
    renderRoutes(routes, {
      route: '/',
      user: makeAdminMe({ mfaEnabled: false, mfaSetupRequired: true }),
    })
    await landsOn('mfa at /account/mfa?required=1&returnTo=%2Fadmin')
  })

  it('RootRedirect sends an anonymous visitor to /login', async () => {
    renderRoutes(routes, { route: '/', user: null })
    await landsOn('login at /login')
  })
})

describe('gateRedirect', () => {
  it('returns null when no gate applies', () => {
    expect(gateRedirect(makeStudentMe(), '/student', '')).toBeNull()
  })

  it('drops an unsafe destination instead of carrying it', () => {
    const user = makeAdminMe({ mustChangePassword: true })
    expect(gateRedirect(user, '/account/mfa', '?returnTo=%2F%2Fevil')).toBe(
      '/account/password?required=1',
    )
  })
})

describe('sign-out redirect', () => {
  it('sends a signed-out user to a clean /login after an explicit sign-out', async () => {
    const { router, events } = renderRoutes(
      [
        ...routes,
        {
          element: <RequireAuth />,
          children: [{ path: '/signout', element: <SignOutButton /> }],
        },
      ],
      { route: '/signout', user: makeStudentMe() },
    )
    await events.click(await screen.findByRole('button', { name: 'Sign out' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(router.state.location.search).toBe('')
  })
})
