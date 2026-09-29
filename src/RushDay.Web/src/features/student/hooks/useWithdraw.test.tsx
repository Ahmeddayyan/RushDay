import { configure, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { queryKeys } from '@/api/keys'
import type { ModuleSummary, MyEnrolment } from '@/api/types/common'
import { problem } from '@/test/http'
import { makeModule } from '@/test/handlers/modules'
import { createEnrolmentServer, makeMyEnrolment } from '@/test/handlers/student'
import { renderRoutes } from '@/test/render'

import { studentRoutes } from '../routes'

// Pages load through lazy routes and these tests click through whole journeys; under a parallel
// coverage run a first render can take over a second, so both budgets are raised for this file.
configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 20_000 })

const enrolledRow = makeMyEnrolment({
  moduleCode: 'CS3099',
  title: 'Software Engineering Project',
})

function cs3099Card(): HTMLElement {
  return screen.getByRole('link', { name: /^CS3099/ }).closest('li') as HTMLElement
}

function setup({
  module = makeModule(),
  remove,
}: {
  module?: ModuleSummary
  remove?: Parameters<typeof http.delete>[1]
} = {}) {
  const server = createEnrolmentServer({ modules: [module], rows: [enrolledRow] })
  const handlers = [
    ...(remove ? [http.delete('/api/me/enrolments/:code', remove)] : []),
    ...server.handlers,
  ]
  return {
    ...renderRoutes(studentRoutes, { route: '/student/modules?q=CS3099', handlers }),
    server,
  }
}

async function openDialog(events: ReturnType<typeof setup>['events']) {
  await events.click(await screen.findByRole('button', { name: 'Withdraw from CS3099' }))
  return screen.findByRole('alertdialog')
}

describe('useWithdraw and WithdrawDialog', () => {
  it('asks first, with the consequence and re-enrolment while the window is open', async () => {
    const { events } = setup()
    const dialog = await openDialog(events)

    expect(dialog).toHaveAccessibleName('Withdraw from CS3099 Software Engineering Project')
    expect(
      within(dialog).getByText('Withdraw from CS3099? Your place is released immediately.'),
    ).toBeInTheDocument()
    expect(
      within(dialog).getByText(
        'You can re-enrol while places remain until 2 October 2026 at 18:00 (BST).',
      ),
    ).toBeInTheDocument()
    expect(
      within(dialog).getByText('Withdrawal deadline: 30 October 2026 at 17:00 (GMT).'),
    ).toBeInTheDocument()

    await events.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(within(cs3099Card()).getByText('Enrolled')).toBeInTheDocument()
  })

  it('warns that re-enrolment is not possible once the window has closed', async () => {
    const { events } = setup({ module: makeModule({ enrolmentState: 'closed' }) })
    const dialog = await openDialog(events)
    expect(
      within(dialog).getByText(
        "Enrolment for Autumn closed on 2 October 2026 at 18:00 (BST), so you won't be able to re-enrol yourself.",
      ),
    ).toBeInTheDocument()
  })

  it('is optimistic: the row is withdrawn before the server answers', async () => {
    let release: () => void = () => {}
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    const { events, queryClient } = setup({
      remove: async () => {
        await gate
        return new HttpResponse(null, { status: 204 })
      },
    })
    const dialog = await openDialog(events)
    await events.click(within(dialog).getByRole('button', { name: 'Withdraw' }))

    await waitFor(() =>
      expect(
        queryClient
          .getQueryData<MyEnrolment[]>(queryKeys.student.enrolments)
          ?.find((row) => row.moduleCode === 'CS3099')?.status,
      ).toBe('withdrawn'),
    )
    expect(
      await within(cs3099Card()).findByRole('button', { name: 'Enrol again on CS3099' }),
    ).toBeInTheDocument()

    release()
    expect(
      await screen.findByText("You've withdrawn from CS3099. Your place has been released."),
    ).toBeInTheDocument()
  })

  it('rolls back and says why when the server refuses', async () => {
    const { events, queryClient } = setup({
      remove: () =>
        problem('withdrawal-deadline-passed', {
          extensions: { withdrawalDeadlineAt: '2026-09-20T16:00:00.000Z' },
        }),
    })
    const dialog = await openDialog(events)
    await events.click(within(dialog).getByRole('button', { name: 'Withdraw' }))

    expect(
      await screen.findByText(
        'The withdrawal deadline for CS3099 was 20 September 2026 at 17:00 (BST). To withdraw now, contact the academic office.',
      ),
    ).toBeInTheDocument()
    expect(
      queryClient
        .getQueryData<MyEnrolment[]>(queryKeys.student.enrolments)
        ?.find((row) => row.moduleCode === 'CS3099')?.status,
    ).toBe('active')
    expect(
      await within(cs3099Card()).findByRole('button', { name: 'Withdraw from CS3099' }),
    ).toBeInTheDocument()
  })

  it('withdraws through the server and frees the place', async () => {
    const { events, server } = setup()
    const dialog = await openDialog(events)
    await events.click(within(dialog).getByRole('button', { name: 'Withdraw' }))
    await waitFor(() =>
      expect(within(cs3099Card()).getByRole('meter')).toHaveAttribute(
        'aria-valuetext',
        '13 of 30 places left',
      ),
    )
    expect(server.state.requests.withdraw).toBe(1)
    expect(within(cs3099Card()).getByText(/^Withdrawn /)).toBeInTheDocument()
  })
})
