import { expect, expectNoHorizontalScroll, loginAs, test } from './fixtures.ts'

/**
 * The phone layout (05-frontend.md sections 7 and 9.3): at 360 px nothing scrolls sideways on the
 * sign-in page, the dashboard, the catalogue or the marks grid; students get bottom tabs; the drawer
 * traps focus and gives it back. The viewport is pinned to 360 × 780 so the desktop project checks
 * the same layout with a mouse and without touch.
 */

test.use({ viewport: { width: 360, height: 780 } })

test('the sign-in page fits 360 px', async ({ page }) => {
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Sign in', level: 1 })).toBeVisible()
  await expectNoHorizontalScroll(page)
})

test('a student gets the dashboard and catalogue at 360 px, with bottom tabs', async ({ page }) => {
  await loginAs(page, 'student')
  await expect(page.getByText('Average so far', { exact: true })).toBeVisible()
  await expectNoHorizontalScroll(page)

  const tabs = page.getByRole('navigation', { name: 'Shortcuts' })
  await expect(tabs).toBeVisible()
  await expect(tabs.getByRole('link')).toHaveText(['Home', 'Results', 'Timetable', 'Modules'])
  // The sidebar gives way to the drawer below 1024 px.
  await expect(page.getByRole('navigation', { name: 'Primary' })).toHaveCount(0)

  await tabs.getByRole('link', { name: 'Modules' }).click()
  await expect(page).toHaveURL(/\/student\/modules$/)
  await expect(page.getByText(/^\d+ modules$/)).toBeVisible()
  await expectNoHorizontalScroll(page)
})

test('the marks grid fits 360 px', async ({ page }) => {
  await loginAs(page, 'lecturer')
  await page.goto('/lecturer/modules/CS3001/marks')
  await expect(page.getByRole('textbox', { name: /^Mark for / }).first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Save marks' })).toBeVisible()
  await expectNoHorizontalScroll(page)
})

test('the navigation drawer traps focus and returns it', async ({ page }) => {
  await loginAs(page, 'student')
  const menu = page.getByRole('button', { name: 'Open navigation' })
  await expect(menu).toHaveAttribute('aria-expanded', 'false')

  await menu.focus()
  await page.keyboard.press('Enter')
  const drawer = page.getByRole('dialog', { name: 'Navigation' })
  await expect(drawer).toBeVisible()
  await expect(drawer.getByRole('navigation', { name: 'Primary' })).toBeVisible()

  const focusInDrawer = () =>
    page.evaluate(() => {
      const dialog = document.querySelector('[role="dialog"]')
      return dialog !== null && dialog.contains(document.activeElement)
    })
  await expect.poll(focusInDrawer).toBe(true)
  // Forwards and backwards past both ends: focus never leaves the drawer.
  for (let step = 0; step < 16; step++) {
    await page.keyboard.press('Tab')
    expect(await focusInDrawer()).toBe(true)
  }
  for (let step = 0; step < 16; step++) {
    await page.keyboard.press('Shift+Tab')
    expect(await focusInDrawer()).toBe(true)
  }

  await page.keyboard.press('Escape')
  await expect(drawer).toHaveCount(0)
  await expect(menu).toBeFocused()
})
