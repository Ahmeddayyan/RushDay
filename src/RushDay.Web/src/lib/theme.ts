import { useCallback, useEffect, useState } from 'react'

/** 'system' means follow prefers-color-scheme and store nothing (05-frontend.md section 9.1). */
export type ThemeMode = 'system' | 'light' | 'dark'
export type ResolvedTheme = 'light' | 'dark'

export const THEME_STORAGE_KEY = 'rushday.theme'

/** localStorage can be missing or throw (private mode, disabled storage, quota); never let that break the page. */
function readStored(): ThemeMode {
  try {
    const value = window.localStorage.getItem(THEME_STORAGE_KEY)
    return value === 'light' || value === 'dark' ? value : 'system'
  } catch {
    return 'system'
  }
}

function writeStored(mode: ThemeMode): void {
  try {
    if (mode === 'system') window.localStorage.removeItem(THEME_STORAGE_KEY)
    else window.localStorage.setItem(THEME_STORAGE_KEY, mode)
  } catch {
    // Storage unavailable: the choice still applies for this page view.
  }
}

export function readSystemTheme(): ResolvedTheme {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return 'light'
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

/** Applies the resolved theme to <html data-theme> and mirrors it to <meta name="theme-color">. */
function applyTheme(mode: ThemeMode, resolved: ResolvedTheme): void {
  if (mode === 'system') delete document.documentElement.dataset.theme
  else document.documentElement.dataset.theme = mode

  const light = document.querySelector('meta[name="theme-color"][media*="light"]')
  const dark = document.querySelector('meta[name="theme-color"][media*="dark"]')
  const active = document.querySelector('meta[name="theme-color"]')
  const source = resolved === 'dark' ? (dark ?? active) : (light ?? active)
  const content = source?.getAttribute('content')
  if (active && content) active.setAttribute('content', content)
}

export function useTheme(): {
  mode: ThemeMode
  resolved: ResolvedTheme
  setMode: (mode: ThemeMode) => void
} {
  const [mode, setModeState] = useState<ThemeMode>(readStored)
  const [systemTheme, setSystemTheme] = useState<ResolvedTheme>(readSystemTheme)

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return
    const query = window.matchMedia('(prefers-color-scheme: dark)')
    const onChange = () => setSystemTheme(query.matches ? 'dark' : 'light')
    query.addEventListener('change', onChange)
    return () => query.removeEventListener('change', onChange)
  }, [])

  const resolved = mode === 'system' ? systemTheme : mode

  // Side effect only (DOM mutation): the resolved theme itself is derived above, at render time.
  useEffect(() => {
    applyTheme(mode, resolved)
  }, [mode, resolved])

  const setMode = useCallback((next: ThemeMode) => {
    writeStored(next)
    setModeState(next)
  }, [])

  return { mode, resolved, setMode }
}
