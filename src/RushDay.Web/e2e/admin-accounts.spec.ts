import type { Page } from '@playwright/test'

import {
  api,
  expect,
  newSession,
  onDesktop,
  ROOT_ADMIN,
  ROOT_ADMIN_STATE,
  test,
  uniqueDigits,
} from './fixtures.ts'

/**
 * Accounts (05-frontend.md section 10, `/admin/accounts`; 00-overview.md section 8): a real
 * administrator creates lecturer L9nnnn, provisions an account for them and copies the temporary
 * password; the lecturer's first sign-in forces a password change and then lands on /lecturer;
 * the administrator disables the account and the lecturer's open session ends at its next
 * navigation; the audit log shows who did it. The demo administrator cannot do any of this (demo
 * actors provision only read-only demo accounts, 02-api.md section 8.5), so the page is the root
 * administrator's session saved by global-setup.ts. New records every run: desktop-chromium only.
 */

test.use({ storageState: ROOT_ADMIN_STATE })

const NEW_PASSWORD = 'Violet-Harbour-Engine-58'

async function freeStaffNumber(page: Page): Promise<string> {
  for (;;) {
    const staffNumber = `L9${uniqueDigits(4)}`
    const matches = await api(page).get<{ staffNumber: string }[]>(
      `/api/admin/lecturers?q=${staffNumber}`,
    )
    if (!matches.some((lecturer) => lecturer.staffNumber === staffNumber)) return staffNumber
  }
}

test('an administrator provisions a lecturer, who must change password, then disables them', async ({
  page,
  browser,
}, testInfo) => {
  test.skip(!onDesktop(testInfo), 'creates accounts: desktop-chromium only')
  test.setTimeout(120_000)

  const staffNumber = await freeStaffNumber(page)
  const fullName = `E2E Lecturer ${staffNumber.slice(2)}`
  testInfo.annotations.push({ type: 'lecturer', description: staffNumber })

  await test.step('create the lecturer record', async () => {
    await page.goto('/admin/lecturers')
    await page.getByRole('button', { name: 'Create lecturer' }).click()
    const dialog = page.getByRole('dialog', { name: 'Create lecturer' })
    await dialog.getByLabel(/^Staff number/).fill(staffNumber)
    await dialog.getByLabel(/^Full name/).fill(fullName)
    await dialog.getByLabel(/^Department/).fill('CS')
    await dialog.getByRole('button', { name: 'Create lecturer' }).click()
    await expect(page.getByText(`Created ${staffNumber} Dr ${fullName}.`)).toBeVisible()
    await expect(dialog).toBeHidden()
  })

  let temporaryPassword = ''
  await test.step('provision the account and copy the temporary password', async () => {
    await page.goto('/admin/accounts')
    await page.getByRole('button', { name: 'Provision account' }).click()
    const dialog = page.getByRole('dialog', { name: 'Provision account' })
    await dialog.getByLabel(/^Role/).selectOption('Lecturer')
    await dialog.getByRole('combobox', { name: /^Lecturer/ }).fill(staffNumber)
    await dialog.getByRole('option', { name: new RegExp(staffNumber) }).click()
    await expect(dialog.getByLabel(/^Username/)).toHaveValue(staffNumber)
    await dialog.getByRole('button', { name: 'Provision account' }).click()

    const shown = page.getByRole('dialog', { name: `Temporary password for ${staffNumber}` })
    await expect(shown).toContainText('The person must change it')
    temporaryPassword = (await shown.getByTestId('temporary-password').textContent())?.trim() ?? ''
    expect(temporaryPassword).toMatch(/^[A-Za-z2-9]{16}$/)
    await shown.getByRole('button', { name: 'Copy' }).click()
    // Headless browsers may refuse the clipboard; either answer shows the button did its job.
    await expect(shown.getByRole('status')).toHaveText(
      /Copied to the clipboard\.|Copying is blocked here/,
    )
    await shown.getByRole('button', { name: 'Done' }).click()
    await expect(shown).toBeHidden()
  })

  const lecturerContext = await newSession(browser, testInfo)
  const lecturer = await lecturerContext.newPage()

  await test.step('the lecturer signs in and is made to change the password', async () => {
    await lecturer.goto('/login')
    await lecturer.getByLabel('Student number, staff number or admin username').fill(staffNumber)
    await lecturer.getByLabel('Password', { exact: true }).fill(temporaryPassword)
    await lecturer.getByRole('button', { name: 'Sign in', exact: true }).click()

    await expect(lecturer).toHaveURL(/\/account\/password\?required=1/)
    await expect(
      lecturer.getByText(
        'Your administrator set a temporary password. Choose a new one to continue.',
      ),
    ).toBeVisible()
    // Navigation stays hidden until the password is changed.
    await expect(lecturer.getByRole('navigation', { name: 'Primary' })).toHaveCount(0)

    await lecturer.getByLabel(/^Current password/).fill(temporaryPassword)
    await lecturer.getByLabel(/^New password/).fill(NEW_PASSWORD)
    await lecturer.getByLabel(/^Confirm new password/).fill(NEW_PASSWORD)
    await lecturer.getByRole('button', { name: 'Change password' }).click()

    await expect(lecturer).toHaveURL(/\/lecturer$/)
    await expect(lecturer.getByRole('heading', { level: 1 })).toBeVisible()
    await expect(lecturer.getByRole('navigation', { name: 'Primary' })).toBeVisible()
  })

  await test.step('the administrator disables the account', async () => {
    await page.goto(`/admin/accounts?q=${staffNumber}`)
    const row = page.getByRole('row').filter({ hasText: staffNumber })
    await expect(row).toHaveCount(1)
    await row.getByRole('button', { name: `Actions for ${staffNumber}` }).click()
    await page.getByRole('menuitem', { name: /^Disable/ }).click()
    const confirm = page.getByRole('alertdialog', { name: `Disable ${staffNumber}?` })
    await confirm.getByRole('button', { name: 'Disable account' }).click()
    await expect(page.getByText(`Disabled ${staffNumber}.`)).toBeVisible()
    await expect(row).toContainText('Disabled')
  })

  await test.step("the lecturer's open session ends at the next navigation", async () => {
    // The stamp check is due when the last one is older than Auth:SecurityStampIntervalMinutes (0
    // here), counted in whole seconds (02-api.md section 2.1), so a navigation within the same second
    // as the session's previous check still passes; the next one after that lands on /login.
    await expect(async () => {
      await lecturer.goto('/lecturer/modules')
      await expect(lecturer).toHaveURL(/\/login(\?|$)/, { timeout: 1_000 })
    }).toPass({ timeout: 15_000 })
    await expect(lecturer.getByRole('heading', { name: 'Sign in', level: 1 })).toBeVisible()
    expect((await lecturer.request.get('/api/auth/me')).status()).toBe(401)
  })

  await test.step('the audit log records the administrator’s actions', async () => {
    await page.goto(`/admin/audit?actor=${ROOT_ADMIN.username}`)
    const rows = page.getByRole('row')
    await expect(
      rows.filter({ hasText: 'account.provisioned' }).filter({ hasText: staffNumber }),
    ).toBeVisible()
    await expect(
      rows.filter({ hasText: 'account.disabled' }).filter({ hasText: staffNumber }),
    ).toBeVisible()
    await expect(rows.filter({ hasText: 'lecturer.created' }).first()).toBeVisible()
  })

  await lecturerContext.close()
})
