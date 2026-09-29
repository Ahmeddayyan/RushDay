import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { makeLecturerMe } from '@/test/factories'
import {
  buildLecturerModule,
  lecturerHandlers,
  makeMarksRowFixture,
  resetLecturerStore,
} from '@/test/handlers/lecturer'
import { renderWithProviders } from '@/test/render'

import { SubmitDialog } from './SubmitDialog'

afterEach(() => {
  resetLecturerStore()
})

function renderDialog(props: Partial<Parameters<typeof SubmitDialog>[0]> = {}) {
  const onSubmitted = vi.fn()
  const result = renderWithProviders(
    <SubmitDialog
      code="CS3001"
      myRole="leader"
      leader="Dr Grace Hopper"
      total={3}
      onSubmitted={onSubmitted}
      {...props}
    />,
    { user: makeLecturerMe(), handlers: lecturerHandlers },
  )
  return { ...result, onSubmitted }
}

describe('SubmitDialog', () => {
  it('shows only a caption to a teacher, naming the leader', () => {
    renderDialog({ myRole: 'teacher' })
    expect(screen.getByText('Ask Dr Grace Hopper to submit.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit module' })).not.toBeInTheDocument()
  })

  it('is disabled with no active enrolments', () => {
    renderDialog({ total: 0 })
    expect(screen.getByRole('button', { name: 'Submit module' })).toBeDisabled()
    expect(screen.getByText('No students enrolled')).toBeInTheDocument()
  })

  it('lists missing students and disables submit until every mark is entered', async () => {
    buildLecturerModule({
      code: 'CS3001',
      rowCount: 3,
      rows: [
        makeMarksRowFixture(0, { outcome: 'mark', mark: 70, gradeStatus: 'draft', version: 1 }),
        makeMarksRowFixture(1, { outcome: null, mark: null }),
        makeMarksRowFixture(2, { outcome: null, mark: null }),
      ],
    })
    const { events } = renderDialog({ total: 3 })

    await events.click(screen.getByRole('button', { name: 'Submit module' }))

    expect(await screen.findByText('2 students have no mark yet:')).toBeInTheDocument()
    expect(screen.getByText('S000002')).toBeInTheDocument()
    expect(screen.getByText('S000003')).toBeInTheDocument()
    expect(
      screen.getByText(
        'Students without a mark can be recorded as Absent or Deferred from the outcome menu; the academic office will follow up.',
      ),
    ).toBeInTheDocument()

    const confirmButtons = screen.getAllByRole('button', { name: 'Submit module' })
    const confirm = confirmButtons[confirmButtons.length - 1]!
    expect(confirm).toBeDisabled()
  })

  it('submits when every active student has an outcome', async () => {
    buildLecturerModule({
      code: 'CS3001',
      rowCount: 2,
      rows: [
        makeMarksRowFixture(0, { outcome: 'mark', mark: 70, gradeStatus: 'draft', version: 1 }),
        makeMarksRowFixture(1, { outcome: 'absent', mark: null, gradeStatus: 'draft', version: 1 }),
      ],
    })
    const { events, onSubmitted } = renderDialog({ total: 2 })

    await events.click(screen.getByRole('button', { name: 'Submit module' }))
    await waitFor(() => expect(screen.queryByText(/no mark yet/)).not.toBeInTheDocument())

    const confirmButtons = screen.getAllByRole('button', { name: 'Submit module' })
    const confirm = confirmButtons[confirmButtons.length - 1]!
    expect(confirm).toBeEnabled()
    await events.click(confirm)

    await waitFor(() => expect(onSubmitted).toHaveBeenCalledTimes(1))
    expect(onSubmitted.mock.calls[0]![0]).toMatchObject({ code: 'CS3001', status: 'submitted', gradeCount: 2 })
  })
})
