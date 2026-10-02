import { expect, loginAs, test } from './fixtures.ts'

/**
 * The two pages that tell the load story (05-frontend.md sections 10 and 11): `/story`, public, with
 * the chart cards, their table twins and the glossary; `/admin/ops`, live, with the health summary,
 * samples that keep arriving and the database pool meter.
 */

test('/story renders the chart cards, their tables and the glossary without signing in', async ({
  page,
}) => {
  await page.goto('/story')
  await expect(
    page.getByRole('heading', { name: 'How RushDay holds up under load', level: 1 }),
  ).toBeVisible()
  await expect(page.getByText(/^Measured on /)).toBeVisible()

  const figures = page.getByRole('figure')
  await expect(figures.first()).toBeVisible()
  expect(await figures.count()).toBeGreaterThanOrEqual(2)

  // Every chart has a table twin behind its Chart/Table toggle.
  const toggles = page.getByRole('tab', { name: 'Table' })
  await expect(toggles.first()).toBeVisible()
  await toggles.first().click()
  await expect(toggles.first()).toHaveAttribute('aria-selected', 'true')
  const table = figures.first().getByRole('table')
  await expect(table).toBeVisible()
  await expect(table.getByRole('row')).not.toHaveCount(0)
  await expect(
    figures
      .first()
      .getByText(/Before \(v0\)/)
      .first(),
  ).toBeVisible()

  const glossary = page.getByRole('heading', { name: 'Terms used on this page' })
  await expect(glossary).toBeVisible()
  await expect(page.getByText('19 of 20 requests were faster than this.')).toBeVisible()
  await expect(page.getByRole('button', { name: /account menu/ })).toHaveCount(0)
})

test('/admin/ops shows the health summary, keeps sampling and shows the pool meter', async ({
  page,
}) => {
  await loginAs(page, 'admin')
  const firstSample = page.waitForResponse(
    (response) => response.url().endsWith('/api/admin/ops/metrics') && response.ok(),
  )
  await page.goto('/admin/ops')
  await expect(page.getByRole('heading', { name: 'Operations', level: 1 })).toBeVisible()
  const first = (await (await firstSample).json()) as { sampledAt: string }

  // One of the three plain-language states, with the last minute in words.
  const health = page.getByRole('status').filter({ hasText: /^(Running normally|Busy|Struggling)/ })
  await expect(health).toBeVisible()
  await expect(health).toContainText(/Last minute: [\d,]+ requests, [\d.]+% succeeded/)

  await expect(page.getByRole('meter', { name: 'Database connections in use' })).toBeVisible()
  await expect(page.getByText(/^\d+ of \d+ connections in use$/)).toBeVisible()
  await expect(
    page.getByText(/^Times a page gave up waiting for the database since the server started: /),
  ).toBeVisible()

  // The page polls every 5 seconds: the next sample is a newer one, and the age resets.
  const next = await page.waitForResponse(
    (response) => response.url().endsWith('/api/admin/ops/metrics') && response.ok(),
    { timeout: 15_000 },
  )
  const second = (await next.json()) as { sampledAt: string }
  expect(Date.parse(second.sampledAt)).toBeGreaterThan(Date.parse(first.sampledAt))
  await expect(page.getByText(/^Sampled [0-2]s ago\.$/)).toBeVisible()
})
