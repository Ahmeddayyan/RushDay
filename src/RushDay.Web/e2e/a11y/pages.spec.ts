import { AxeBuilder } from '@axe-core/playwright'
import type { Page, TestInfo } from '@playwright/test'

import { expect, loginAs, setTheme, test, type Role } from '../fixtures.ts'

/**
 * axe (`wcag2a`, `wcag2aa`, `wcag22aa`) on every page, in both themes, on both projects
 * (05-frontend.md section 12): zero `serious` or `critical` violations. Every finding of any impact
 * is attached to the report as JSON. Reduced motion is emulated so no scan catches a transition
 * half-way (the app honours it, section 12). Read-only pages and demo accounts only.
 */

const PAGES: Record<'public' | Role, string[]> = {
  public: ['/login', '/story', '/accessibility', '/forbidden', '/no-such-page'],
  student: [
    '/student',
    '/student/results',
    '/student/timetable',
    '/student/modules',
    '/student/modules/CS3001',
    '/announcements',
    '/account',
    '/account/password',
  ],
  lecturer: [
    '/lecturer',
    '/lecturer/modules',
    '/lecturer/modules/CS3001',
    '/lecturer/modules/CS3001/marks',
    '/lecturer/modules/CS3001/announcements',
    '/account/mfa',
  ],
  admin: [
    '/admin',
    '/admin/students',
    '/admin/students/S000001',
    '/admin/modules',
    '/admin/modules/CS3001',
    '/admin/modules/CS3001/roster',
    '/admin/modules/CS3001/marks',
    '/admin/lecturers',
    '/admin/accounts',
    '/admin/enrolment',
    '/admin/results',
    '/admin/announcements',
    '/admin/audit',
    '/admin/ops',
    '/admin/settings',
  ],
}

const TAGS = ['wcag2a', 'wcag2aa', 'wcag22aa']

interface Finding {
  path: string
  id: string
  impact: string | null | undefined
  help: string
  targets: string[]
}

/** Waits until the page has its heading and nothing is loading any more. */
async function settled(page: Page, path: string) {
  await expect(page.locator('h1').first(), `${path} has a heading`).toBeAttached()
  await expect(page.locator('[aria-busy="true"]'), `${path} finished loading`).toHaveCount(0)
  if (path === '/story') await expect(page.getByRole('figure').first()).toBeVisible()
  if (path === '/admin/ops') await expect(page.getByRole('meter').first()).toBeVisible()
}

async function scan(page: Page, paths: string[], theme: 'light' | 'dark', testInfo: TestInfo) {
  const findings: Finding[] = []
  let rulesPassed = 0
  for (const path of paths) {
    await page.goto(path)
    await settled(page, path)
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme)
    const results = await new AxeBuilder({ page }).withTags(TAGS).analyze()
    // A scan that evaluated nothing (axe not injected) must not pass as "no violations".
    expect(results.passes.length, `axe evaluated rules on ${path}`).toBeGreaterThan(0)
    rulesPassed += results.passes.length
    for (const violation of results.violations) {
      findings.push({
        path,
        id: violation.id,
        impact: violation.impact,
        help: violation.help,
        targets: violation.nodes.map((node) => node.target.join(' ')),
      })
    }
  }

  await testInfo.attach('axe-findings.json', {
    body: JSON.stringify({ theme, pages: paths, findings }, null, 2),
    contentType: 'application/json',
  })
  const blocking = findings.filter((f) => f.impact === 'serious' || f.impact === 'critical')
  const summary =
    `${paths.length} pages, ${rulesPassed} rule passes, ${findings.length} findings, ` +
    `${blocking.length} serious or critical`
  testInfo.annotations.push({ type: 'axe', description: summary })
  console.log(`axe ${testInfo.project.name} ${theme} ${testInfo.title}: ${summary}`)
  expect(
    blocking.map((f) => `${f.path} [${f.impact}] ${f.id}: ${f.help} (${f.targets.join(', ')})`),
    `serious or critical axe violations in the ${theme} theme`,
  ).toEqual([])
}

for (const theme of ['light', 'dark'] as const) {
  test.describe(`${theme} theme`, () => {
    test.beforeEach(async ({ page }) => {
      await setTheme(page, theme)
      await page.emulateMedia({ colorScheme: theme, reducedMotion: 'reduce' })
    })

    test('public pages', async ({ page }, testInfo) => {
      await scan(page, PAGES.public, theme, testInfo)
    })

    for (const role of ['student', 'lecturer', 'admin'] as const) {
      test(`${role} pages`, async ({ page }, testInfo) => {
        test.setTimeout(180_000)
        await loginAs(page, role)
        await scan(page, PAGES[role], theme, testInfo)
      })
    }
  })
}
