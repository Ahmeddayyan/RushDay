import { useState } from 'react'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'

import type { MarksStatusValue } from '@/api/types/common'
import { makeLecturerMe } from '@/test/factories'
import {
  buildLecturerModule,
  lecturerHandlers,
  makeMarksRowFixture,
  putMarksCallSizes,
  resetLecturerStore,
} from '@/test/handlers/lecturer'
import { renderWithProviders } from '@/test/render'

import { MarksGrid } from './MarksGrid'

afterEach(() => {
  resetLecturerStore()
})

/** Mirrors how `MarksTab` drives the grid: `page`/`q` live in the parent, `MarksGrid` is controlled. */
function Harness({
  code,
  status = 'draft',
}: {
  code: string
  status?: MarksStatusValue
}) {
  const [page, setPage] = useState(1)
  const [q, setQ] = useState('')
  return (
    <MarksGrid code={code} status={status} page={page} q={q} onPageChange={setPage} onQueryChange={setQ} />
  )
}

function renderGrid(code: string, status: MarksStatusValue = 'draft') {
  return renderWithProviders(<Harness code={code} status={status} />, {
    user: makeLecturerMe(),
    handlers: lecturerHandlers,
  })
}

describe('MarksGrid', () => {
  it('validates a mark as an integer 0..100', async () => {
    buildLecturerModule({ code: 'CS3001', rowCount: 3 })
    const { events } = renderGrid('CS3001')

    const input = await screen.findByLabelText('Mark for Student 1')
    await events.type(input, '150')
    expect(await screen.findByText('Enter a whole number from 0 to 100.')).toBeInTheDocument()

    await events.clear(input)
    await events.type(input, '85')
    expect(screen.queryByText('Enter a whole number from 0 to 100.')).not.toBeInTheDocument()
    expect(await screen.findByText('1 unsaved')).toBeInTheDocument()
  })

  it('clears and disables the mark when the outcome is not Mark', async () => {
    buildLecturerModule({ code: 'CS3001', rowCount: 3 })
    const { events } = renderGrid('CS3001')

    const input = await screen.findByLabelText('Mark for Student 1')
    await events.type(input, '72')
    expect(input).toHaveValue('72')

    const outcome = screen.getByLabelText('Outcome for Student 1')
    await events.selectOptions(outcome, 'absent')
    expect(input).toBeDisabled()
    expect(input).toHaveValue('')
  })

  it('moves focus to the next mark input on Enter, and back on ArrowUp', async () => {
    buildLecturerModule({ code: 'CS3001', rowCount: 3 })
    renderGrid('CS3001')

    const first = await screen.findByLabelText('Mark for Student 1')
    const second = screen.getByLabelText('Mark for Student 2')
    first.focus()
    fireEvent.keyDown(first, { key: 'Enter' })
    expect(second).toHaveFocus()

    fireEvent.keyDown(second, { key: 'ArrowUp' })
    expect(first).toHaveFocus()
  })

  it('keeps dirty count across pages', async () => {
    buildLecturerModule({ code: 'CS3001', rowCount: 150 })
    const { events } = renderGrid('CS3001')

    const first = await screen.findByLabelText('Mark for Student 1')
    await events.type(first, '60')
    expect(await screen.findByText('1 unsaved')).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByLabelText('Mark for Student 101')

    const onPage2 = screen.getByLabelText('Mark for Student 101')
    await events.type(onPage2, '61')
    expect(await screen.findByText('2 unsaved')).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Previous page' }))
    await screen.findByLabelText('Mark for Student 1')
    expect(screen.getByLabelText('Mark for Student 1')).toHaveValue('60')
    expect(screen.getByText('2 unsaved')).toBeInTheDocument()
  })

  it('locks every row once the module is not draft or noStudents', async () => {
    buildLecturerModule({
      code: 'CS3001',
      rowCount: 2,
      rows: [
        makeMarksRowFixture(0, { outcome: 'mark', mark: 70, gradeStatus: 'submitted', version: 2 }),
        makeMarksRowFixture(1, { outcome: 'mark', mark: 80, gradeStatus: 'submitted', version: 2 }),
      ],
    })
    renderGrid('CS3001', 'submitted')

    expect(
      await screen.findByText("Marks for this module are locked and can't be edited here."),
    ).toBeInTheDocument()
    expect(await screen.findByLabelText('Mark for Student 1')).toBeDisabled()
    expect(screen.getByLabelText('Outcome for Student 1')).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Save marks' })).toBeDisabled()
  })

  it('keeps a stale-mark row dirty and highlighted after a rejected save', async () => {
    const fixture = buildLecturerModule({ code: 'CS3001', rowCount: 2 })
    const { events } = renderGrid('CS3001')

    const input = await screen.findByLabelText('Mark for Student 1')
    await events.type(input, '55')
    await events.click(screen.getByRole('button', { name: 'Save marks' }))
    await waitFor(() => expect(screen.getByText('All changes saved')).toBeInTheDocument())

    // Someone else changes the same row's stored version before the next save.
    fixture.rows[0]!.version = (fixture.rows[0]!.version ?? 1) + 5

    await events.clear(input)
    await events.type(input, '77')
    await events.click(screen.getByRole('button', { name: 'Save marks' }))

    expect(
      await screen.findByText('Someone else changed this mark. Review the highlighted row and save again.'),
    ).toBeInTheDocument()
    expect(screen.getByText('1 unsaved')).toBeInTheDocument()
    const row = input.closest('tr')!
    expect(row).toHaveAttribute('data-stale', 'true')
    expect(row).toHaveAttribute('data-dirty', 'true')
  })

  it('restores a sessionStorage mirror after a remount', async () => {
    buildLecturerModule({ code: 'CS3001', rowCount: 2 })
    const { events, unmount } = renderGrid('CS3001')

    const input = await screen.findByLabelText('Mark for Student 1')
    await events.type(input, '64')
    await waitFor(() => expect(sessionStorage.getItem('rushday.marks.CS3001')).not.toBeNull())

    unmount()

    renderGrid('CS3001')
    expect(
      await screen.findByText('Restored 1 unsaved mark from before you were signed out.'),
    ).toBeInTheDocument()
    expect(await screen.findByLabelText('Mark for Student 1')).toHaveValue('64')
    expect(screen.getByText('1 unsaved')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save marks' })).toBeEnabled()
  })

  it('saves 600 dirty rows out of 1,300, paged, as two chunked requests', async () => {
    buildLecturerModule({ code: 'CS3001', rowCount: 1300 })
    const { events } = renderGrid('CS3001')

    async function dirtyCurrentPage() {
      const inputs = await screen.findAllByLabelText(/^Mark for /)
      expect(inputs.length).toBe(100)
      inputs[0]!.focus()
      const text = inputs.map((_, index) => 50 + (index % 40)).join('\n')
      fireEvent.paste(inputs[0]!, { clipboardData: { getData: () => text } })
      await waitFor(() => expect(inputs[inputs.length - 1]).toHaveValue(String(50 + ((99) % 40))))
    }

    for (let page = 0; page < 6; page += 1) {
      await dirtyCurrentPage()
      if (page < 5) {
        await events.click(screen.getByRole('button', { name: 'Next page' }))
        await waitFor(() => expect(screen.getAllByLabelText(/^Mark for /)).toHaveLength(100))
      }
    }

    expect(await screen.findByText('600 unsaved')).toBeInTheDocument()

    await events.click(screen.getByRole('button', { name: 'Save marks' }))

    await waitFor(() => expect(screen.getByText('All changes saved')).toBeInTheDocument())
    expect(putMarksCallSizes).toEqual([500, 100])
  }, 30_000)
})
