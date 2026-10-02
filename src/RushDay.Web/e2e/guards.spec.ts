import { expect, loginAs, test } from './fixtures.ts'

/**
 * Route guards and hosting (05-frontend.md sections 4 and 5.2): a signed-in user outside their
 * role's area gets a real /forbidden page, unknown paths get the not-found page, a deep link
 * survives a full reload, and an unknown API path is JSON, never the SPA shell.
 */

const FORBIDDEN = "You don't have access to that page"

test('a student sent to an admin page sees /forbidden', async ({ page }) => {
  await loginAs(page, 'student')
  await page.goto('/admin')
  await expect(page).toHaveURL(/\/forbidden$/)
  await expect(page.getByRole('heading', { name: FORBIDDEN, level: 1 })).toBeVisible()
})

test('a lecturer sent to a student page sees /forbidden', async ({ page }) => {
  await loginAs(page, 'lecturer')
  await page.goto('/student')
  await expect(page).toHaveURL(/\/forbidden$/)
  await expect(page.getByRole('heading', { name: FORBIDDEN, level: 1 })).toBeVisible()
})

test('an unknown route shows the not-found page', async ({ page }) => {
  const response = await page.goto('/no-such-page/at-all')
  expect(response?.status()).toBe(200)
  await expect(
    page.getByRole('heading', { name: "That page doesn't exist", level: 1 }),
  ).toBeVisible()
  await expect(page).toHaveTitle('Page not found · RushDay')
})

test('a deep link survives a full reload', async ({ page }) => {
  await loginAs(page, 'student')
  await page.goto('/student/results')
  await expect(page.getByRole('heading', { name: 'Results', level: 1 })).toBeVisible()

  const response = await page.reload()
  expect(response?.status()).toBe(200)
  expect(response?.headers()['content-type']).toContain('text/html')
  await expect(page).toHaveURL(/\/student\/results$/)
  await expect(page.getByRole('heading', { name: 'Results', level: 1 })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Autumn 2025/26', level: 2 })).toBeVisible()
})

test('an unknown API path answers JSON 404, not the SPA', async ({ page }) => {
  const response = await page.request.get('/api/does-not-exist')
  expect(response.status()).toBe(404)
  expect(response.headers()['content-type']).toContain('application/problem+json')
  const problem = (await response.json()) as { type: string; status: number }
  expect(problem).toMatchObject({ type: 'urn:rushday:not-found', status: 404 })
})
