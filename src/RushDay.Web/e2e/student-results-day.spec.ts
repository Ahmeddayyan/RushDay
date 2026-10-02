import { readFile } from 'node:fs/promises'

import { expect, loginAs, test } from './fixtures.ts'

/**
 * The demo student S000001 on results day (05-frontend.md section 10): the dashboard's average and
 * indicative band, last year's completed modules, results grouped by semester, this semester's
 * timetable and its calendar file. Read-only, so it runs on both projects.
 */

test.beforeEach(async ({ page }) => {
  await loginAs(page, 'student')
})

test('the dashboard shows the average so far, the band and last year’s modules', async ({
  page,
}) => {
  const average = page.getByText('Average so far', { exact: true })
  await expect(average).toBeVisible()
  // The figure right under the label: a credit-weighted average with one decimal.
  await expect(average.locator('xpath=following-sibling::p[1]')).toHaveText(/^\d{1,3}\.\d$/)
  await expect(page.getByText(/^Indicative band: /)).toBeVisible()

  const completed = page.getByRole('region', { name: '2025/26' })
  await expect(completed).toBeVisible()
  await expect(completed.getByRole('link').first()).toBeVisible()
  await expect(completed.getByText(/^Mark \d{1,3} \(/).first()).toBeVisible()
})

test('results are grouped by semester, newest first', async ({ page }) => {
  await page.goto('/student/results')
  await expect(page.getByRole('heading', { name: 'Results', level: 1 })).toBeVisible()

  const autumn = page.getByRole('region', { name: 'Autumn 2025/26' })
  await expect(autumn.getByRole('heading', { name: 'Autumn 2025/26', level: 2 })).toBeVisible()
  await expect(autumn.getByRole('table', { name: 'Autumn 2025/26' })).toBeVisible()
  await expect(autumn.getByRole('row')).not.toHaveCount(0)
  await expect(page.getByText('Average so far')).toBeVisible()
})

test('the timetable shows the current semester and downloads as a calendar', async ({ page }) => {
  await page.goto('/student/timetable')
  await expect(page.getByRole('heading', { name: 'Timetable', level: 1 })).toBeVisible()
  // The caption under the heading (the grid's own caption repeats it for screen readers).
  await expect(page.getByText('Autumn 2026/27 timetable', { exact: true }).first()).toBeVisible()
  // S000001 is in the CS3001 autumn cohort, so the week has classes.
  await expect(page.getByText(/CS3001/).first()).toBeAttached()

  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: /Add to calendar \(\.ics\)/ }).click()
  const file = await download
  expect(file.suggestedFilename()).toMatch(/\.ics$/)
  const path = await file.path()
  const text = await readFile(path, 'utf8')
  expect(text.startsWith('BEGIN:VCALENDAR')).toBe(true)
  expect(text).toContain('SUMMARY:CS3001')
  expect(text).toContain('RRULE:FREQ=WEEKLY;COUNT=12')
})
