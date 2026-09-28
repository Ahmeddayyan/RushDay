import { act, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { setSystemDarkMode } from '@/test/setup'

import { THEME_STORAGE_KEY, useTheme } from './theme'

describe('useTheme', () => {
  it('follows the OS preference when nothing is stored', () => {
    setSystemDarkMode(true)
    const { result } = renderHook(() => useTheme())

    expect(result.current.mode).toBe('system')
    expect(result.current.resolved).toBe('dark')
    expect(document.documentElement.dataset.theme).toBeUndefined()
  })

  it('sets an explicit preference, applies data-theme and persists it', () => {
    const { result } = renderHook(() => useTheme())

    act(() => result.current.setMode('dark'))

    expect(result.current.mode).toBe('dark')
    expect(result.current.resolved).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
  })

  it('clears the stored key when set back to system', () => {
    const { result } = renderHook(() => useTheme())

    act(() => result.current.setMode('light'))
    act(() => result.current.setMode('system'))

    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBeNull()
    expect(document.documentElement.dataset.theme).toBeUndefined()
  })

  it('still resolves and applies a theme when localStorage throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('denied', 'SecurityError')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('denied', 'QuotaExceededError')
    })

    const { result } = renderHook(() => useTheme())
    expect(result.current.mode).toBe('system')

    act(() => result.current.setMode('dark'))
    expect(result.current.mode).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
  })
})
