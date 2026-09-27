import { createContext, useContext } from 'react'

export type Theme = 'light' | 'dark'
/** What the user chose. 'system' means follow prefers-color-scheme and store nothing. */
export type ThemePreference = Theme | 'system'

export interface ThemeContextValue {
  /** The theme actually applied to <html data-theme>. */
  theme: Theme
  preference: ThemePreference
  setPreference: (preference: ThemePreference) => void
  /** Flips between light and dark, saving the result as an explicit preference. */
  toggleTheme: () => void
}

export const THEME_STORAGE_KEY = 'rushday.theme'

export const ThemeContext = createContext<ThemeContextValue | null>(null)

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext)
  if (!context) throw new Error('useTheme must be used inside <ThemeProvider>.')
  return context
}

/** localStorage can be missing or throw (private mode, disabled storage, quota); never let that break the page. */
export function readStoredPreference(): ThemePreference {
  try {
    const value = window.localStorage.getItem(THEME_STORAGE_KEY)
    return value === 'light' || value === 'dark' ? value : 'system'
  } catch {
    return 'system'
  }
}

export function writeStoredPreference(preference: ThemePreference): void {
  try {
    if (preference === 'system') window.localStorage.removeItem(THEME_STORAGE_KEY)
    else window.localStorage.setItem(THEME_STORAGE_KEY, preference)
  } catch {
    // Storage unavailable: the choice still applies for this page view.
  }
}

export const darkSchemeQuery = '(prefers-color-scheme: dark)'

export function readSystemTheme(): Theme {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return 'light'
  return window.matchMedia(darkSchemeQuery).matches ? 'dark' : 'light'
}
