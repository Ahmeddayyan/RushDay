import type { Page } from '@playwright/test'

import {
  api,
  expect,
  newSession,
  onDesktop,
  ROOT_ADMIN_STATE,
  test,
  uniqueDigits,
} from './fixtures.ts'
import { freshTotp } from './totp.ts'

/**
 * A new administrator's first sign-in (02-api.md section 2.4, 05-frontend.md section 10,
 * 00-overview.md section 8): provisioned by the root administrator (a real one, so the new account
 * is real), they are forced to change the temporary password, then forced to set up two-step
 * verification; the key is read from the page and the code computed with RFC 6238 (e2e/totp.ts).
 * After signing out, signing in asks for a code and lands on /admin. Desktop-chromium only.
 */

test.use({ storageState: ROOT_ADMIN_STATE })

const NEW_PASSWORD = 'Copper-Lighthouse-Rain-93'

async function signInThroughForm(page: Page, username: string, password: string) {
  await page.goto('/login')
  await page.getByLabel('Student number, staff number or admin username').fill(username)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
}

test('a new administrator is forced through a password change and TOTP setup', async ({
  page,
  browser,
}, testInfo) => {
  test.skip(!onDesktop(testInfo), 'creates accounts: desktop-chromium only')
  test.setTimeout(120_000)

  const username = `e2e-adm-${uniqueDigits(6)}`
  testInfo.annotations.push({ type: 'administrator', description: username })
  const { temporaryPassword } = await api(page).post<{ temporaryPassword: string }>(
    '/api/admin/accounts',
    { username, displayName: `E2E Administrator ${username.slice(-6)}`, role: 'Admin' },
  )

  const context = await newSession(browser, testInfo)
  const admin = await context.newPage()
  let sharedKey = ''
  let enabledStep = 0

  await test.step('first sign-in: forced password change', async () => {
    await signInThroughForm(admin, username, temporaryPassword)
    await expect(admin).toHaveURL(/\/account\/password\?required=1/)
    await admin.getByLabel(/^Current password/).fill(temporaryPassword)
    await admin.getByLabel(/^New password/).fill(NEW_PASSWORD)
    await admin.getByLabel(/^Confirm new password/).fill(NEW_PASSWORD)
    await admin.getByRole('button', { name: 'Change password' }).click()
  })

  await test.step('then forced two-step setup with a computed code', async () => {
    await expect(admin).toHaveURL(/\/account\/mfa\?required=1/)
    await expect(
      admin.getByText('Administrators must use two-step verification. Set it up to continue.'),
    ).toBeVisible()
    await expect(
      admin.getByRole('img', { name: 'QR code for your authenticator app' }),
    ).toBeVisible()
    sharedKey = ((await admin.getByLabel('Setup key').textContent()) ?? '').trim()
    expect(sharedKey.replace(/\s/g, '')).toMatch(/^[A-Za-z2-7]{16,}$/)

    const { code, step } = freshTotp(sharedKey)
    await admin.getByLabel('Verification code').fill(code)
    await admin.getByRole('button', { name: 'Turn on two-step verification' }).click()
    await expect(admin.getByText('Two-step verification is on.')).toBeVisible()
    await expect(admin).toHaveURL(/\/admin$/)
    enabledStep = step
  })

  await test.step('sign out, then sign in again with a code', async () => {
    await admin.getByRole('button', { name: /account menu/ }).click()
    await admin.getByRole('menuitem', { name: 'Sign out' }).click()
    await expect(admin).toHaveURL(/\/login$/)

    await signInThroughForm(admin, username, NEW_PASSWORD)
    await expect(admin.getByRole('heading', { name: 'Enter your verification code' })).toBeVisible()
    // A later step than the one `enable` used: the server never accepts a step twice.
    const { code } = freshTotp(sharedKey, enabledStep)
    await admin.getByLabel('Verification code').fill(code)
    await admin.getByRole('button', { name: 'Verify' }).click()

    await expect(admin).toHaveURL(/\/admin$/)
    await expect(admin.getByRole('navigation', { name: 'Primary' })).toBeVisible()
  })

  await context.close()
})
