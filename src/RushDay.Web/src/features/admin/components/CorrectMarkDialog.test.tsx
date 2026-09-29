import { useState } from 'react'
import { configure, screen, waitFor, within } from '@testing-library/react'
import { http } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { makeAdminMe } from '@/test/factories'
import { createAdminMock, makeAdminMarksRow, makeAdminMarksSheet } from '@/test/handlers/admin'
import { problem } from '@/test/http'
import { renderWithProviders } from '@/test/render'

import { CorrectMarkDialog, type CorrectionTarget } from './CorrectMarkDialog'

// Admin pages render large lazy route trees; under coverage on a CI runner the first render
// can take longer than the default second.
configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 30_000 })

const target: CorrectionTarget = {
  code: 'CS3001',
  studentNumber: 'S000001',
  studentName: 'Student 1',
  current: { outcome: 'mark', mark: 50 },
  live: true,
}

function Harness({ initial }: { initial: CorrectionTarget }) {
  const [current, setCurrent] = useState<CorrectionTarget | null>(initial)
  return <CorrectMarkDialog target={current} onOpenChange={(open) => !open && setCurrent(null)} />
}

function render(
  initial: CorrectionTarget = target,
  extra: Parameters<typeof renderWithProviders>[1] = {},
) {
  const mock = createAdminMock({
    marks: {
      CS3001: makeAdminMarksSheet({
        status: 'published',
        rows: [makeAdminMarksRow(0, { gradeStatus: 'published', mark: 50 })],
      }),
    },
  })
  const result = renderWithProviders(<Harness initial={initial} />, {
    user: makeAdminMe(),
    ...extra,
    handlers: [...(extra.handlers ?? []), ...mock.handlers],
  })
  return { ...result, mock }
}

describe('CorrectMarkDialog', () => {
  it('warns that a live correction reaches the student immediately', async () => {
    render()
    const dialog = await screen.findByRole('alertdialog', {
      name: "Correct Student 1's mark for CS3001",
    })
    expect(dialog).toHaveTextContent('S000001 currently has 50.')
    expect(
      within(dialog).getByText('The student sees the corrected mark immediately.'),
    ).toBeInTheDocument()
  })

  it('says nothing about immediacy for a mark students cannot see yet', async () => {
    render({ ...target, live: false })
    await screen.findByRole('alertdialog')
    expect(
      screen.queryByText('The student sees the corrected mark immediately.'),
    ).not.toBeInTheDocument()
  })

  it('clears and disables the mark for Absent or Deferred', async () => {
    const { events } = render()
    const dialog = await screen.findByRole('alertdialog')
    const mark = within(dialog).getByLabelText(/^Mark/)
    expect(mark).toHaveValue('50')
    await events.selectOptions(within(dialog).getByLabelText(/^Outcome/), 'absent')
    expect(mark).toHaveValue('')
    expect(mark).toBeDisabled()
    await events.selectOptions(within(dialog).getByLabelText(/^Outcome/), 'mark')
    expect(mark).toBeEnabled()
  })

  it('validates the mark and the reason before sending', async () => {
    const { events, mock } = render()
    const dialog = await screen.findByRole('alertdialog')
    const mark = within(dialog).getByLabelText(/^Mark/)
    await events.clear(mark)
    await events.type(mark, '101')
    await events.type(within(dialog).getByLabelText(/^Reason/), 'short')
    await events.click(within(dialog).getByRole('button', { name: 'Correct mark' }))
    expect(await within(dialog).findByText('Enter a whole mark from 0 to 100.')).toBeInTheDocument()
    expect(within(dialog).getByText('Give a reason of at least 10 characters.')).toBeInTheDocument()
    expect(mock.requests.filter((request) => request.method === 'POST')).toHaveLength(0)
  })

  it('sends the correction with its reason and confirms the change', async () => {
    const { events, mock } = render()
    const dialog = await screen.findByRole('alertdialog')
    const mark = within(dialog).getByLabelText(/^Mark/)
    await events.clear(mark)
    await events.type(mark, '72')
    await events.type(within(dialog).getByLabelText(/^Reason/), 'Exam board re-marked question 4')
    await events.click(within(dialog).getByRole('button', { name: 'Correct mark' }))
    expect(await screen.findByText('Corrected S000001 on CS3001: 50 → 72.')).toBeInTheDocument()
    expect(
      mock.calls('POST', '/api/admin/results/modules/CS3001/marks/S000001/correct')[0]?.body,
    ).toEqual({ outcome: 'mark', mark: 72, reason: 'Exam board re-marked question 4' })
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
  })

  it('records an outcome without a mark', async () => {
    const { events, mock } = render()
    const dialog = await screen.findByRole('alertdialog')
    await events.selectOptions(within(dialog).getByLabelText(/^Outcome/), 'deferred')
    await events.type(within(dialog).getByLabelText(/^Reason/), 'Mitigating circumstances accepted')
    await events.click(within(dialog).getByRole('button', { name: 'Correct mark' }))
    expect(
      await screen.findByText('Corrected S000001 on CS3001: 50 → Deferred.'),
    ).toBeInTheDocument()
    expect(
      mock.calls('POST', '/api/admin/results/modules/CS3001/marks/S000001/correct')[0]?.body,
    ).toEqual({ outcome: 'deferred', mark: null, reason: 'Mitigating circumstances accepted' })
  })

  it('keeps the dialog open with the server’s reason when the grade is still a draft', async () => {
    const { events } = render(target, {
      handlers: [
        http.post('/api/admin/results/modules/:code/marks/:studentNumber/correct', () =>
          problem('module-not-submitted'),
        ),
      ],
    })
    const dialog = await screen.findByRole('alertdialog')
    await events.type(within(dialog).getByLabelText(/^Reason/), 'Exam board re-marked question 4')
    await events.click(within(dialog).getByRole('button', { name: 'Correct mark' }))
    expect(
      await within(dialog).findByText(
        "This module is still in draft; there's nothing to return or correct yet.",
      ),
    ).toBeInTheDocument()
  })
})
