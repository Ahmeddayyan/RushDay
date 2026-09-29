import type { Browser, Page, TestInfo } from '@playwright/test'

import { demoStudent, expect, loginAs, newSession, onDesktop, test, type Api } from './fixtures.ts'
import { HOT_MODULE, resetHotModule } from './hot-module.ts'

/**
 * Results day end to end (05-frontend.md section 10, 00-overview.md section 8): the administrator
 * override-enrols two students on CS3099; the leader L00001 enters one mark and one Absent in the
 * grid, saves and submits, and the inputs lock; the administrator publishes Spring 2026/27 now
 * through PublishDialog with the exam-board confirmation; the student sees the mark; the
 * administrator corrects it with a reason and the student sees the new mark labelled Amended.
 * Publishing is irreversible for the demo data, so this runs on desktop-chromium only; CS3099 is
 * still reset before and after (unpublish, return to draft, withdraw), so the enrolment journey
 * and a retry find it as the seed left it.
 */

/**
 * Two first-year students who have never had a CS3099 grade, picked afresh for every attempt:
 * first-years hold no level-3 module from 2025/26, so CS3099 is new to them. A student this journey
 * marked before (a retry, a second run on one database) would keep that grade's correction date
 * through the return to draft and show "Amended" before this run corrects anything.
 */
async function twoFreshFirstYears(admin: Api): Promise<[string, string]> {
  const chosen: string[] = []
  while (chosen.length < 2) {
    // S000100 to S000298 with a number of the form 3k + 1: year 1 in the seed (01 section 6 step 12).
    const number = `S${String(100 + 3 * Math.floor(Math.random() * 67)).padStart(6, '0')}`
    if (chosen.includes(number)) continue
    const view = await admin.get<{ grades: { moduleCode: string }[] }>(
      `/api/admin/students/${number}`,
    )
    if (!view.grades.some((grade) => grade.moduleCode === HOT_MODULE)) chosen.push(number)
  }
  return [chosen[0] ?? '', chosen[1] ?? '']
}

const OVERRIDE_REASON = 'End-to-end: marks journey cohort.'

test.beforeAll(async ({ adminApi }, testInfo) => {
  test.skip(!onDesktop(testInfo), 'publishes results: desktop-chromium only')
  await resetHotModule(adminApi)
})

test.afterAll(async ({ adminApi }, testInfo) => {
  if (onDesktop(testInfo)) await resetHotModule(adminApi)
})

async function openAs(browser: Browser, testInfo: TestInfo, who: Parameters<typeof loginAs>[1]) {
  const context = await newSession(browser, testInfo)
  const page = await context.newPage()
  await loginAs(page, who)
  return page
}

function markRow(page: Page, studentNumber: string) {
  return page.getByRole('row').filter({ hasText: studentNumber })
}

test('a lecturer submits marks, the office publishes and corrects, the student sees Amended', async ({
  page,
  browser,
  adminApi,
}, testInfo) => {
  test.setTimeout(120_000)
  const [MARKED, ABSENT] = await twoFreshFirstYears(adminApi)
  testInfo.annotations.push({
    type: 'students',
    description: `${MARKED} (mark), ${ABSENT} (Absent)`,
  })
  // Different marks on every run, so a correction is always a change.
  const mark = 55 + Math.floor(Math.random() * 20)
  const corrected = mark + 7

  await test.step('the administrator override-enrols two students (API)', async () => {
    for (const studentNumber of [MARKED, ABSENT]) {
      await adminApi.post(`/api/admin/students/${studentNumber}/enrolments`, {
        moduleCode: HOT_MODULE,
        reason: OVERRIDE_REASON,
        forceCapacity: true,
      })
    }
  })

  await test.step('L00001 enters a mark and an Absent, saves and submits', async () => {
    await loginAs(page, 'lecturer')
    await page.goto(`/lecturer/modules/${HOT_MODULE}/marks`)
    await expect(markRow(page, MARKED)).toBeVisible()
    await expect(markRow(page, ABSENT)).toBeVisible()

    await markRow(page, MARKED)
      .getByRole('textbox', { name: /^Mark for / })
      .fill(String(mark))
    await markRow(page, ABSENT)
      .getByRole('combobox', { name: /^Outcome for / })
      .selectOption('absent')
    await expect(page.getByText('2 unsaved')).toBeVisible()

    await page.getByRole('button', { name: 'Save marks' }).click()
    await expect(page.getByText('Saved 2 changes.')).toBeVisible()
    await expect(page.getByText('All changes saved')).toBeVisible()

    await page.getByRole('button', { name: 'Submit module' }).click()
    const dialog = page.getByRole('alertdialog', { name: `Submit 2 marks for ${HOT_MODULE}?` })
    await expect(dialog).toContainText('Marks are locked for editing')
    const confirm = dialog.getByRole('button', { name: 'Submit module' })
    await expect(confirm).toBeEnabled()
    await confirm.click()

    await expect(page.getByText(`Submitted 2 marks for ${HOT_MODULE}.`)).toBeVisible()
    await expect(
      page.getByRole('status').filter({ hasText: /^Submitted on .*Marks are locked\./ }),
    ).toBeVisible()
    await expect(markRow(page, MARKED).getByRole('textbox', { name: /^Mark for / })).toBeDisabled()
    await expect(
      markRow(page, ABSENT).getByRole('combobox', { name: /^Outcome for / }),
    ).toBeDisabled()
    await expect(page.getByRole('button', { name: 'Save marks' })).toBeDisabled()
  })

  const admin = await openAs(browser, testInfo, 'admin')

  await test.step('the administrator publishes Spring 2026/27 now', async () => {
    await admin.goto('/admin/results?semester=spring')
    const row = admin.getByRole('row').filter({ hasText: HOT_MODULE })
    await expect(row).toContainText('Ready to publish')

    await admin.getByRole('button', { name: 'Publish results' }).click()
    const dialog = admin.getByRole('alertdialog', { name: /^Publish Spring \d{4}\/\d{2} results$/ })
    await expect(dialog).toContainText('Will publish 1 module (2 marks); 0 modules excluded')
    await expect(dialog.getByLabel('Publish now')).toBeChecked()
    const publish = dialog.getByRole('button', { name: 'Publish 1 module now' })
    await expect(publish).toBeDisabled()
    await dialog.getByLabel('The exam board has approved these marks').check()
    await publish.click()

    await expect(
      admin.getByText('Published: 1 module, 2 marks. Students see them now.'),
    ).toBeVisible()
    await expect(row).toContainText('Live')
  })

  const student = await openAs(browser, testInfo, await demoStudent(page.request, MARKED))
  const resultRow = student
    .getByRole('region', { name: /^Spring \d{4}\/\d{2}$/ })
    .getByRole('row')
    .filter({ hasText: HOT_MODULE })

  await test.step('the student sees the mark', async () => {
    await student.goto('/student/results')
    await expect(resultRow).toBeVisible()
    await expect(resultRow.getByRole('cell').nth(3)).toHaveText(String(mark))
    await expect(resultRow).not.toContainText('Amended')
  })

  await test.step('the administrator corrects the mark with a reason', async () => {
    await admin.goto(`/admin/modules/${HOT_MODULE}/marks`)
    await admin.getByRole('button', { name: new RegExp(`^Correct the mark of ${MARKED} `) }).click()
    const dialog = admin.getByRole('alertdialog')
    await expect(dialog).toContainText('The student sees the corrected mark immediately.')
    await dialog.getByRole('textbox', { name: /^Mark/ }).fill(String(corrected))
    await dialog
      .getByRole('textbox', { name: /^Reason/ })
      .fill('Exam board found an addition error in the script.')
    await dialog.getByRole('button', { name: 'Correct mark' }).click()
    await expect(
      admin.getByText(`Corrected ${MARKED} on ${HOT_MODULE}: ${mark} → ${corrected}.`),
    ).toBeVisible()
  })

  await test.step('the student sees the new mark, labelled Amended', async () => {
    await student.reload()
    await expect(resultRow.getByRole('cell').nth(3)).toHaveText(String(corrected))
    await expect(resultRow.getByText(/^Amended /)).toBeVisible()
  })
})
