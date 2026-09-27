import { expect, test, type Page } from '@playwright/test'

async function expectNoHorizontalScroll(page: Page) {
  const widths = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    client: document.documentElement.clientWidth,
  }))
  expect(widths.scroll).toBeLessThanOrEqual(widths.client)
}

test.describe('at 360 px', () => {
  test.use({ viewport: { width: 360, height: 740 } })

  test('the shell fits without horizontal scroll and the drawer works from the keyboard', async ({
    page,
  }, testInfo) => {
    await page.goto('/')
    await expectNoHorizontalScroll(page)
    await page.screenshot({ path: testInfo.outputPath('dashboard-360.png'), fullPage: true })

    // Below md the sidebar is gone; navigation lives in the drawer, which starts closed.
    await expect(page.getByRole('navigation', { name: 'Primary' })).toHaveCount(0)
    const menu = page.getByRole('button', { name: 'Open navigation' })
    await expect(menu).toHaveAttribute('aria-expanded', 'false')

    await menu.focus()
    await page.keyboard.press('Enter')
    const drawer = page.getByRole('dialog', { name: 'Navigation' })
    await expect(drawer).toBeVisible()
    await expect(drawer.getByRole('link', { name: 'Dashboard' })).toBeFocused()
    // The drawer spans the full viewport height and its backdrop covers the page behind it.
    const box = await drawer.boundingBox()
    expect(box).toMatchObject({ x: 0, y: 0, height: 740 })
    const backdrop = page.getByRole('button', { name: 'Close navigation' }).first()
    expect(await backdrop.boundingBox()).toMatchObject({ x: 0, y: 0, width: 360, height: 740 })
    await page.screenshot({ path: testInfo.outputPath('drawer-360.png') })

    await page.keyboard.press('Escape')
    await expect(drawer).toHaveCount(0)
    await expect(menu).toBeFocused()
  })

  test('the sign-in page fits too', async ({ page }, testInfo) => {
    await page.goto('/login')
    await expect(page.getByRole('heading', { name: 'Sign in to RushDay' })).toBeVisible()
    await expectNoHorizontalScroll(page)
    await page.screenshot({ path: testInfo.outputPath('login-360.png'), fullPage: true })
  })
})

test('the desktop shell renders the sidebar', async ({ page }, testInfo) => {
  await page.goto('/')
  await expect(page.getByRole('navigation', { name: 'Primary' })).toBeVisible()
  await page.screenshot({ path: testInfo.outputPath('dashboard-desktop.png') })
})
