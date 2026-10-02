import { configure, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { queryKeys } from '@/api/keys'
import type { ModuleDetail, ModuleSummary, MyEnrolment } from '@/api/types/common'
import { problem } from '@/test/http'
import { makeModule, makeModuleDetail, moduleHandler } from '@/test/handlers/modules'
import { createEnrolmentServer, makeMyEnrolment } from '@/test/handlers/student'
import { createTestQueryClient, renderRoutes } from '@/test/render'

import { studentRoutes } from '../routes'

// Pages load through lazy routes and these tests click through whole journeys; under a parallel
// coverage run a first render can take over a second, so both budgets are raised for this file.
configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 20_000 })

function cs3099Card(): HTMLElement {
  const link = screen.getByRole('link', { name: /^CS3099/ })
  return link.closest('li') as HTMLElement
}

function setup(
  overrides: Parameters<typeof http.post>[1] | null,
  {
    detail = makeModuleDetail(),
    extra = [],
  }: { detail?: ModuleDetail; extra?: ReturnType<typeof http.get>[] } = {},
) {
  const server = createEnrolmentServer({ modules: [makeModule()], rows: [makeMyEnrolment()] })
  let posts = 0
  const handlers = [
    ...extra,
    ...(overrides
      ? [
          http.post('/api/me/enrolments', (info) => {
            posts += 1
            return overrides(info)
          }),
        ]
      : []),
    moduleHandler([detail]),
    ...server.handlers,
  ]
  const queryClient = createTestQueryClient()
  // The module page was visited before: its live row is cached too.
  queryClient.setQueryData(queryKeys.modules.detail('CS3099'), makeModuleDetail())
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
  const result = renderRoutes(studentRoutes, {
    route: '/student/modules?q=CS3099',
    handlers,
    queryClient,
  })
  return { ...result, server, invalidate, posts: () => posts + server.state.requests.enrol }
}

describe('useEnrol', () => {
  it('is not optimistic: "Enrolling…" while pending, then the caches, the toast and the refetches', async () => {
    let release: () => void = () => {}
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    // The refetch of the enrolments after the 201 is held back, so the row seen below is the one
    // the hook appended to the cache, not the server's.
    let releaseRefetch: () => void = () => {}
    const refetchGate = new Promise<void>((resolve) => {
      releaseRefetch = resolve
    })
    let enrolmentReads = 0
    const serverRow = makeMyEnrolment({
      moduleCode: 'CS3099',
      title: 'Software Engineering Project',
      enrolledAt: '2026-09-29T10:00:00.000Z',
    })
    const { events, queryClient, invalidate, posts } = setup(
      async () => {
        await gate
        return HttpResponse.json(
          { moduleCode: 'CS3099', enrolledAt: '2026-09-29T10:00:00.000Z', placesRemaining: 11 },
          { status: 201 },
        )
      },
      {
        extra: [
          http.get('/api/me/enrolments', async () => {
            enrolmentReads += 1
            if (enrolmentReads === 1) return HttpResponse.json([makeMyEnrolment()])
            await refetchGate
            return HttpResponse.json([serverRow, makeMyEnrolment()])
          }),
        ],
      },
    )

    await events.click(await screen.findByRole('button', { name: 'Enrol on CS3099' }))

    const pending = await within(cs3099Card()).findByRole('button', { name: /Enrolling…/ })
    expect(pending).toBeDisabled()
    expect(pending).toHaveAttribute('aria-busy', 'true')
    // Never "Enrolled" before the server says so.
    expect(within(cs3099Card()).queryByText('Enrolled')).not.toBeInTheDocument()
    expect(
      queryClient
        .getQueryData<MyEnrolment[]>(queryKeys.student.enrolments)
        ?.some((row) => row.moduleCode === 'CS3099'),
    ).toBe(false)

    release()

    expect(await screen.findByText("You're in: CS3099. 11 places left.")).toBeInTheDocument()
    const catalogue = queryClient.getQueryData<ModuleSummary[]>(queryKeys.modules.catalogue)
    expect(catalogue?.find((module) => module.code === 'CS3099')?.placesRemaining).toBe(11)
    const detail = queryClient.getQueryData<ModuleDetail>(queryKeys.modules.detail('CS3099'))
    expect(detail?.placesRemaining).toBe(11)
    expect(detail?.enrolledCount).toBe(19)

    const row = queryClient
      .getQueryData<MyEnrolment[]>(queryKeys.student.enrolments)
      ?.find((item) => item.moduleCode === 'CS3099')
    expect(row).toMatchObject({
      title: 'Software Engineering Project',
      credits: 15,
      semester: 'autumn',
      academicYear: '2026/27',
      status: 'active',
      enrolledAt: '2026-09-29T10:00:00.000Z',
      canWithdraw: true,
      withdrawBlockedReason: null,
      withdrawalDeadlineAt: '2026-10-30T17:00:00.000Z',
    })
    await waitFor(() => expect(within(cs3099Card()).getByText('Enrolled')).toBeInTheDocument())
    releaseRefetch()
    await waitFor(() => expect(enrolmentReads).toBe(2))

    const invalidated = invalidate.mock.calls.map(([filters]) => filters?.queryKey)
    expect(invalidated).toContainEqual(queryKeys.student.enrolments)
    expect(invalidated).toContainEqual(queryKeys.student.dashboard)
    expect(invalidated).not.toContainEqual(queryKeys.modules.catalogue)
    expect(posts()).toBe(1)
  })

  it('409 module-full: "Full", "Filled while you were enrolling" and the live count', async () => {
    const { events, posts } = setup(() => problem('module-full'), {
      detail: makeModuleDetail({ enrolledCount: 30, placesRemaining: 0 }),
    })

    await events.click(await screen.findByRole('button', { name: 'Enrol on CS3099' }))

    const card = cs3099Card()
    expect(await within(card).findByRole('button', { name: 'Full' })).toBeDisabled()
    expect(within(card).getByText('Filled while you were enrolling')).toBeInTheDocument()
    expect(within(card).getByText('Filled just now')).toBeInTheDocument()
    await waitFor(() =>
      expect(within(card).getByRole('meter')).toHaveAttribute(
        'aria-valuetext',
        '0 of 30 places left',
      ),
    )
    expect(posts()).toBe(1)
  })

  it('503 keeps "Enrol" but disables it for Retry-After with a visible count, and never retries by itself', async () => {
    const { events, posts } = setup(() =>
      problem('server-busy', { headers: { 'Retry-After': '2' } }),
    )

    await events.click(await screen.findByRole('button', { name: 'Enrol on CS3099' }))

    const card = cs3099Card()
    expect(
      await within(card).findByText('The portal is busy. Try again in 2s.'),
    ).toBeInTheDocument()
    const button = within(card).getByRole('button', { name: 'Enrol on CS3099' })
    expect(button).toBeDisabled()
    expect(
      await within(card).findByText('The portal is busy. Try again in 1s.', {}, { timeout: 2000 }),
    ).toBeInTheDocument()
    await waitFor(() => expect(button).toBeEnabled(), { timeout: 3000 })
    expect(within(card).getByText('12 places left')).toBeInTheDocument()

    // Nothing was retried in the meantime.
    expect(posts()).toBe(1)
  })

  it('treats a missing Retry-After on 429 as two seconds and toasts other errors', async () => {
    const { events } = setup(() => problem('rate-limited'))
    await events.click(await screen.findByRole('button', { name: 'Enrol on CS3099' }))
    expect(
      await within(cs3099Card()).findByText('The portal is busy. Try again in 2s.'),
    ).toBeInTheDocument()
  })

  it('says why the server refused anything else', async () => {
    const { events } = setup(() =>
      problem('credit-limit-exceeded', {
        extensions: { currentCredits: 60, moduleCredits: 15, limit: 60, semester: 'autumn' },
      }),
    )
    await events.click(await screen.findByRole('button', { name: 'Enrol on CS3099' }))
    expect(
      await screen.findByText('That would take you over 60 credits for Autumn.'),
    ).toBeInTheDocument()
    expect(within(cs3099Card()).getByRole('button', { name: 'Enrol on CS3099' })).toBeEnabled()
  })
})
