import { screen, waitFor } from '@testing-library/react'
import { http } from 'msw'
import type { RouteObject } from 'react-router'
import { describe, expect, it, vi } from 'vitest'

import { RequireAuth } from '@/app/guards'
import { AppShell } from '@/components/layout/AppShell'
import { makeAdminMe, makeLecturerMe } from '@/test/factories'
import { authMock, TEST_SHARED_KEY, TEST_TOTP_CODE } from '@/test/handlers/auth'
import { problem } from '@/test/http'
import { renderRoutes } from '@/test/render'

import { Component as MfaSetupPage } from './MfaSetupPage'

const toDataURL = vi.hoisted(() =>
  vi.fn((text: string) => Promise.resolve(`data:image/png;base64,${btoa(text)}`)),
)
vi.mock('qrcode', () => ({ toDataURL }))

const routes: RouteObject[] = [
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppShell />,
        children: [
          { path: '/account/mfa', element: <MfaSetupPage /> },
          { path: '/account', element: <h1>Account page</h1> },
          { path: '/admin/*', element: <h1>Admin overview</h1> },
        ],
      },
    ],
  },
]

const gatedAdmin = () => makeAdminMe({ mfaEnabled: false, mfaSetupRequired: true })

describe('MfaSetupPage', () => {
  it('starts setup once and shows the QR code drawn by qrcode and the key as text', async () => {
    let setups = 0
    const { events } = renderRoutes(routes, {
      route: '/account/mfa?required=1',
      user: gatedAdmin(),
      handlers: [
        http.post('/api/auth/mfa/setup', () => {
          setups += 1
          return undefined
        }),
      ],
    })

    expect(
      await screen.findByText(
        'Administrators must use two-step verification. Set it up to continue.',
      ),
    ).toBeInTheDocument()
    const image = await screen.findByRole('img', { name: 'QR code for your authenticator app' })
    expect(image.getAttribute('src')).toMatch(/^data:image\/png;base64,/)
    expect(toDataURL).toHaveBeenCalledWith(
      expect.stringContaining('otpauth://totp/RushDay:registry.admin'),
      expect.any(Object),
    )
    expect(screen.getByText(TEST_SHARED_KEY)).toBeInTheDocument()
    expect(screen.getByText("Can't scan? Type this key instead.")).toBeInTheDocument()
    expect(setups).toBe(1)
    expect(screen.queryByRole('navigation', { name: 'Primary' })).not.toBeInTheDocument()

    // Copy puts the key, without spaces, on the clipboard.
    const writeText = vi.fn(() => Promise.resolve())
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } })
    await events.click(screen.getByRole('button', { name: 'Copy the key' }))
    expect(writeText).toHaveBeenCalledWith(TEST_SHARED_KEY.replaceAll(' ', ''))
    expect(await screen.findByText('Copied')).toBeInTheDocument()
  })

  it('rejects a wrong code and keeps the page', async () => {
    const { events } = renderRoutes(routes, {
      route: '/account/mfa?required=1',
      user: gatedAdmin(),
    })
    await screen.findByText(TEST_SHARED_KEY)
    await events.type(screen.getByLabelText('Verification code'), '000000')
    await events.click(screen.getByRole('button', { name: 'Turn on two-step verification' }))
    expect(
      await screen.findByText(
        "That code didn't work. Check the time on your phone and try the newest code.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Verification code')).toHaveAttribute('aria-invalid', 'true')
  })

  it('turns two-step verification on and continues to returnTo', async () => {
    const { events, router } = renderRoutes(routes, {
      route: '/account/mfa?required=1&returnTo=%2Fadmin%2Fresults',
      user: gatedAdmin(),
    })
    await screen.findByText(TEST_SHARED_KEY)
    await events.type(screen.getByLabelText('Verification code'), TEST_TOTP_CODE)
    await events.click(screen.getByRole('button', { name: 'Turn on two-step verification' }))
    expect(await screen.findByText('Two-step verification is on.')).toBeInTheDocument()
    await waitFor(() => expect(router.state.location.pathname).toBe('/admin/results'))
    expect(authMock.user?.mfaEnabled).toBe(true)
  })

  it('says so when the account already has it on', async () => {
    renderRoutes(routes, { route: '/account/mfa', user: makeLecturerMe({ mfaEnabled: true }) })
    expect(
      await screen.findByText('Two-step verification is on for this account.'),
    ).toBeInTheDocument()
  })

  it('explains that demo accounts cannot enable it', async () => {
    renderRoutes(routes, {
      route: '/account/mfa',
      user: makeAdminMe({ isDemo: true, mfaEnabled: false }),
    })
    expect(
      await screen.findByText("Demo accounts can't enable two-step verification."),
    ).toBeInTheDocument()
  })

  it('offers Retry when setup fails', async () => {
    const { events } = renderRoutes(routes, {
      route: '/account/mfa',
      user: makeLecturerMe(),
      handlers: [http.post('/api/auth/mfa/setup', () => problem('internal-error'), { once: true })],
    })
    await events.click(await screen.findByRole('button', { name: 'Try again' }))
    expect(await screen.findByText(TEST_SHARED_KEY)).toBeInTheDocument()
  })
})
