import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { Countdown } from './Countdown'

describe('Countdown', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(Date.parse('2026-09-28T08:49:30Z'))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('states the absolute instant and zone, hides the ticking digits and throttles the status sentence', () => {
    const { container } = render(
      <Countdown
        target="2026-09-28T09:00:00Z"
        timeZone="Europe/London"
        sentencePrefix="Results publish at"
      />,
    )

    // The static sentence (09:00 UTC is 10:00 BST).
    expect(screen.getByText('Results publish at 28 September 2026 at 10:00 (BST).')).toHaveClass(
      'sr-only',
    )
    // The digits are hidden from assistive technology.
    const digits = container.querySelector('[aria-hidden="true"]')
    expect(digits).toHaveTextContent('10min')
    expect(digits).toHaveTextContent('30sec')

    const status = screen.getByRole('status')
    expect(status).toHaveTextContent('About 11 minutes to go')

    // Thirty seconds later the digits have moved on, the sentence has not.
    act(() => {
      vi.advanceTimersByTime(29_000)
    })
    expect(digits).toHaveTextContent('01sec')
    expect(status).toHaveTextContent('About 11 minutes to go')

    // Crossing the minute changes it exactly once.
    act(() => {
      vi.advanceTimersByTime(1_000)
    })
    expect(status).toHaveTextContent('About 10 minutes to go')
    act(() => {
      vi.advanceTimersByTime(59_000)
    })
    expect(status).toHaveTextContent('About 10 minutes to go')
  })

  it('shows days for long countdowns and the elapsed text at the end', () => {
    render(
      <Countdown
        target="2026-10-01T08:49:30Z"
        timeZone="Europe/London"
        elapsedText="Results are being released"
        variant="inline"
      />,
    )
    expect(screen.getByText('3d 00:00:00')).toHaveAttribute('aria-hidden', 'true')
    expect(screen.getByRole('status')).toHaveTextContent('About 3 days to go')
  })

  it('announces the elapsed text once the instant has passed', () => {
    render(
      <Countdown
        target="2026-09-28T08:49:31Z"
        timeZone="Europe/London"
        elapsedText="Results are being released"
      />,
    )
    act(() => {
      vi.advanceTimersByTime(1_000)
    })
    expect(screen.getByRole('status')).toHaveTextContent('Results are being released')
  })
})
