import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'

import {
  ThemeContext,
  darkSchemeQuery,
  readStoredPreference,
  readSystemTheme,
  writeStoredPreference,
  type Theme,
  type ThemeContextValue,
  type ThemePreference,
} from './ThemeContext'

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreferenceState] = useState<ThemePreference>(readStoredPreference)
  const [systemTheme, setSystemTheme] = useState<Theme>(readSystemTheme)

  // Follow the OS while no explicit preference is stored.
  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return
    const query = window.matchMedia(darkSchemeQuery)
    const onChange = (event: MediaQueryListEvent) =>
      setSystemTheme(event.matches ? 'dark' : 'light')
    query.addEventListener('change', onChange)
    return () => query.removeEventListener('change', onChange)
  }, [])

  const theme: Theme = preference === 'system' ? systemTheme : preference

  // index.html applies the stored theme before first paint; this keeps <html> in sync afterwards.
  useEffect(() => {
    document.documentElement.dataset.theme = theme
  }, [theme])

  const setPreference = useCallback((next: ThemePreference) => {
    setPreferenceState(next)
    writeStoredPreference(next)
  }, [])

  const toggleTheme = useCallback(() => {
    setPreference(theme === 'dark' ? 'light' : 'dark')
  }, [setPreference, theme])

  const value = useMemo<ThemeContextValue>(
    () => ({ theme, preference, setPreference, toggleTheme }),
    [theme, preference, setPreference, toggleTheme],
  )

  return <ThemeContext value={value}>{children}</ThemeContext>
}
