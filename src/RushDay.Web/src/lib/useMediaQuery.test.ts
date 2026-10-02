import { act, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { useMediaQuery } from './useMediaQuery'

// src/test/setup.ts installs a fresh matchMedia stub before every test, so nothing to restore.
describe('useMediaQuery', () => {
  it('reports the query and follows its changes', () => {
    let matches = false
    const listeners = new Set<() => void>()
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      writable: true,
      value: vi.fn((media: string) => ({
        get matches() {
          return matches
        },
        media,
        addEventListener: (_: string, listener: () => void) => listeners.add(listener),
        removeEventListener: (_: string, listener: () => void) => listeners.delete(listener),
      })),
    })

    const { result, unmount } = renderHook(() => useMediaQuery('(max-width: 767.98px)'))
    expect(result.current).toBe(false)

    act(() => {
      matches = true
      listeners.forEach((listener) => listener())
    })
    expect(result.current).toBe(true)

    unmount()
    expect(listeners.size).toBe(0)
  })

  it('is false where matchMedia does not exist', () => {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      writable: true,
      value: undefined,
    })
    const { result } = renderHook(() => useMediaQuery('(max-width: 767.98px)'))
    expect(result.current).toBe(false)
  })
})
