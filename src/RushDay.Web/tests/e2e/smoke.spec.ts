import { expect, test } from '@playwright/test'

// Assumes the API is running on the configured baseURL and serving the built SPA (see playwright.config.ts).

test('the shell loads with the RushDay title and primary navigation', async ({ page }) => {
  await page.goto('/')

  await expect(page).toHaveTitle(/RushDay/)

  const nav = page.getByRole('navigation', { name: 'Primary' })
  await expect(nav).toBeVisible()
  await expect(nav.getByRole('link', { name: 'Dashboard' })).toBeVisible()
  await expect(nav.getByRole('link', { name: 'Modules' })).toBeVisible()
  await expect(page.getByRole('main')).toContainText('Dashboard')
})

test('deep links are answered by the SPA, not a 404', async ({ page }) => {
  const response = await page.goto('/login')

  expect(response?.status()).toBe(200)
  await expect(page.getByRole('heading', { name: 'Sign in to RushDay' })).toBeVisible()
})
