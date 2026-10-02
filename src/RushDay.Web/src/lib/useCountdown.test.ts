import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { aboutSentence, RELEASE_SPREAD_MS, splitDuration, useCountdown } from './useCountdown'

const NOW = Date.parse('2026-09-28T08:59:00Z')

describe('useCountdown', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(NOW)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('counts down against the server clock, not the browser clock', () => {
    // The browser is two minutes slow: the server already reads 09:01.
    const { result } = renderHook(() =>
      useCountdown('2026-09-28T09:05:00Z', { serverOffsetMs: 2 * 60_000 }),
    )
    expect(result.current?.remainingMs).toBe(4 * 60_000)
    expect(result.current).toMatchObject({ hours: 0, minutes: 4, seconds: 0, elapsed: false })
  })

  it('ticks every second', () => {
    const { result } = renderHook(() => useCountdown('2026-09-28T09:00:00Z'))
    expect(result.current?.remainingMs).toBe(60_000)
    act(() => {
      vi.advanceTimersByTime(3_000)
    })
    expect(result.current?.remainingMs).toBe(57_000)
  })

  it('fires onElapsed exactly once, after a random delay between 0 and 30 s', () => {
    vi.spyOn(Math, 'random').mockReturnValue(0.5)
    const onElapsed = vi.fn()
    const { result } = renderHook(() => useCountdown('2026-09-28T09:00:00Z', { onElapsed }))

    act(() => {
      vi.advanceTimersByTime(60_000)
    })
    expect(result.current?.elapsed).toBe(true)
    expect(onElapsed).not.toHaveBeenCalled()

    act(() => {
      vi.advanceTimersByTime(RELEASE_SPREAD_MS / 2 - 1_000)
    })
    expect(onElapsed).not.toHaveBeenCalled()

    act(() => {
      vi.advanceTimersByTime(1_000)
    })
    expect(onElapsed).toHaveBeenCalledTimes(1)

    act(() => {
      vi.advanceTimersByTime(5 * 60_000)
    })
    expect(onElapsed).toHaveBeenCalledTimes(1)
  })

  it('never waits longer than the spread', () => {
    vi.spyOn(Math, 'random').mockReturnValue(0.999)
    const onElapsed = vi.fn()
    renderHook(() => useCountdown('2026-09-28T08:59:01Z', { onElapsed }))
    act(() => {
      vi.advanceTimersByTime(1_000)
    })
    act(() => {
      vi.advanceTimersByTime(RELEASE_SPREAD_MS)
    })
    expect(onElapsed).toHaveBeenCalledTimes(1)
  })

  it('does not fire for an instant that had already passed when it mounted', () => {
    const onElapsed = vi.fn()
    const { result } = renderHook(() => useCountdown('2026-09-28T08:00:00Z', { onElapsed }))
    expect(result.current?.elapsed).toBe(true)
    act(() => {
      vi.advanceTimersByTime(60_000)
    })
    expect(onElapsed).not.toHaveBeenCalled()
  })

  it('returns null without a target', () => {
    const { result } = renderHook(() => useCountdown(null))
    expect(result.current).toBeNull()
  })
})

describe('splitDuration and aboutSentence', () => {
  it('splits milliseconds into days, hours, minutes and seconds', () => {
    expect(splitDuration(((2 * 24 + 3) * 3600 + 4 * 60 + 5) * 1000)).toEqual({
      days: 2,
      hours: 3,
      minutes: 4,
      seconds: 5,
    })
    expect(splitDuration(-5)).toEqual({ days: 0, hours: 0, minutes: 0, seconds: 0 })
  })

  it('describes the time left in whole minutes, hours or days', () => {
    expect(aboutSentence(0)).toBe('Due now')
    expect(aboutSentence(0, 'Results are being released')).toBe('Results are being released')
    expect(aboutSentence(30_000)).toBe('Less than a minute to go')
    expect(aboutSentence(10 * 60_000 + 30_000)).toBe('About 11 minutes to go')
    expect(aboutSentence(5 * 3600_000)).toBe('About 5 hours to go')
    expect(aboutSentence(3 * 24 * 3600_000)).toBe('About 3 days to go')
  })
})
