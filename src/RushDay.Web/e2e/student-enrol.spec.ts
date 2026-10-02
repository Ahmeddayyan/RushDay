import { demoStudent, expect, loginAs, test } from './fixtures.ts'
import { HOT_MODULE, hotModule, resetHotModule, setHotModuleCapacity } from './hot-module.ts'

/**
 * Enrolment (05-frontend.md sections 6.3 and 10): S000010 finds CS3099 in the catalogue, enrols
 * (the button reads "Enrolling…" until the server answers, then "Enrolled", with the toast), sees
 * it on the module page, withdraws through the confirmation and gets the place back; once the
 * module is full the button says so. Counts are read from the module page and the API, never from
 * the catalogue, whose places are cached for 30 seconds. Runs on both projects: CS3099 is reset
 * before and after.
 */

const STUDENT = 'S000010'

test.beforeAll(async ({ adminApi }) => {
  await resetHotModule(adminApi)
})

test.afterAll(async ({ adminApi }) => {
  await resetHotModule(adminApi)
})

test('a student enrols on CS3099, withdraws, and meets a full module', async ({
  page,
  adminApi,
}) => {
  const before = await hotModule(adminApi)
  expect(before.placesRemaining).toBeGreaterThan(0)
  await loginAs(page, await demoStudent(page.request, STUDENT))

  await test.step('find CS3099 in the catalogue', async () => {
    await page.goto('/student/modules')
    await page.getByLabel('Search by code or title').fill(HOT_MODULE)
    await expect(page).toHaveURL(new RegExp(`[?&]q=${HOT_MODULE}`))
    await expect(page.getByText(/^1 of \d+ modules$/)).toBeVisible()
  })

  const card = page.getByRole('listitem').filter({ hasText: HOT_MODULE })

  await test.step('enrol: "Enrolling…" until the server answers, then "Enrolled"', async () => {
    // Hold the POST until the pending state has been seen, so it is observed however fast the API is.
    let release: () => void = () => {}
    const held = new Promise<void>((resolve) => (release = resolve))
    await page.route('**/api/me/enrolments', async (route) => {
      if (route.request().method() !== 'POST') return route.fallback()
      await held
      return route.continue()
    })

    // "Enrol again" when an earlier run (or the other project) left a withdrawn row.
    await card
      .getByRole('button', { name: new RegExp(`^Enrol( again)? on ${HOT_MODULE}$`) })
      .click()
    await expect(card.getByRole('button', { name: 'Enrolling…' })).toBeDisabled()
    release()

    await expect(card.getByText('Enrolled', { exact: true })).toBeVisible()
    await expect(page.getByText(new RegExp(`^You're in: ${HOT_MODULE}\\.`))).toBeVisible()
    await expect(card.getByRole('button', { name: `Withdraw from ${HOT_MODULE}` })).toBeVisible()
    await page.unroute('**/api/me/enrolments')
  })

  const places = page.getByText(/^\d+ of \d+ places? left$/)

  await test.step('the module page shows the enrolment and the live count', async () => {
    await card.getByRole('link', { name: new RegExp(HOT_MODULE) }).click()
    await expect(page).toHaveURL(new RegExp(`/student/modules/${HOT_MODULE}$`))
    await expect(page.getByText(/^Enrolled since /)).toBeVisible()
    await expect(places).toHaveText(
      `${before.placesRemaining - 1} of ${before.capacity} places left`,
    )
  })

  await test.step('withdraw through the dialog; the place comes back', async () => {
    await page.getByRole('button', { name: `Withdraw from ${HOT_MODULE}` }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog).toContainText(
      `Withdraw from ${HOT_MODULE}? Your place is released immediately.`,
    )
    await dialog.getByRole('button', { name: 'Withdraw', exact: true }).click()

    await expect(
      page.getByText(new RegExp(`^You've withdrawn from ${HOT_MODULE}\\.`)),
    ).toBeVisible()
    await expect(page.getByText(/^You withdrew on /)).toBeVisible()
    await expect(places).toHaveText(`${before.placesRemaining} of ${before.capacity} places left`)
    expect((await hotModule(adminApi)).enrolledCount).toBe(before.enrolledCount)
  })

  await test.step('with capacity set to the enrolled count, the button says Full', async () => {
    const now = await hotModule(adminApi)
    await setHotModuleCapacity(adminApi, now.enrolledCount)
    await page.reload()

    await expect(page.getByRole('button', { name: 'Full', exact: true })).toBeDisabled()
    await expect(
      page.getByText('Full. Places free up when students withdraw; there is no waiting list yet.'),
    ).toBeVisible()
    await expect(places).toHaveText(
      `0 of ${now.enrolledCount} ${now.enrolledCount === 1 ? 'place' : 'places'} left`,
    )
  })
})
