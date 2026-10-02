import { demoCredentials, demoStudent, expect, test, type Credentials } from './fixtures.ts'
import type { Page } from '@playwright/test'

/**
 * Sign-in through the real form (the only journey that does; the others use `loginAs`), the
 * wrong-password copy with the lockout hint from the third failure, the `returnTo` round trip,
 * sign-out, and `/login` for someone already signed in (05-frontend.md sections 5 and 10).
 */

async function fillSignIn(page: Page, { username, password }: Credentials) {
  await page.getByLabel('Student number, staff number or admin username').fill(username)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
}

test('a student signs in through the form and lands on the dashboard', async ({ page }) => {
  const student = await demoCredentials(page.request, 'student')
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Sign in', level: 1 })).toBeVisible()

  await fillSignIn(page, student)

  await expect(page).toHaveURL(/\/student$/)
  await expect(page.getByRole('heading', { level: 1 })).toContainText(
    /Good (morning|afternoon|evening)/,
  )
})

test('a wrong password says so, and from the third attempt explains the pause', async ({
  page,
}) => {
  // A demo account: failures never lock it (02-api.md section 2.3), so the run can repeat.
  const { username } = await demoStudent(page.request, 'S000002')
  await page.goto('/login')
  const alert = page.getByRole('alert').filter({ hasText: 'Incorrect username or password.' })

  for (let attempt = 1; attempt <= 3; attempt++) {
    const answered = page.waitForResponse(
      (response) =>
        response.url().endsWith('/api/auth/login') && response.request().method() === 'POST',
    )
    await fillSignIn(page, { username, password: `not-the-password-${attempt}` })
    expect((await answered).status()).toBe(401)
    await expect(alert).toBeVisible()
    if (attempt < 3) await expect(alert).not.toContainText('Still stuck?')
  }

  await expect(alert).toContainText(
    'Still stuck? Repeated failed attempts can pause sign-in for up to 15 minutes.',
  )
  await expect(page).toHaveURL(/\/login$/)
})

test('a deep link while signed out returns there after signing in', async ({ page }) => {
  const student = await demoCredentials(page.request, 'student')
  await page.goto('/student')
  await expect(page).toHaveURL(/\/login\?returnTo=%2Fstudent$/)

  await fillSignIn(page, student)

  await expect(page).toHaveURL(/\/student$/)
})

test('signing out ends the session, and /login sends a signed-in visitor home', async ({
  page,
}) => {
  const student = await demoCredentials(page.request, 'student')
  await page.goto('/login')
  await fillSignIn(page, student)
  await expect(page).toHaveURL(/\/student$/)

  // Already signed in: the sign-in page is skipped.
  await page.goto('/login')
  await expect(page).toHaveURL(/\/student$/)

  await page.getByRole('button', { name: /account menu/ }).click()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()
  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByRole('heading', { name: 'Sign in', level: 1 })).toBeVisible()

  // The session is gone on the server too, not only in the page.
  const me = await page.request.get('/api/auth/me')
  expect(me.status()).toBe(401)
  await page.goto('/student')
  await expect(page).toHaveURL(/\/login\?returnTo=%2Fstudent$/)
})
