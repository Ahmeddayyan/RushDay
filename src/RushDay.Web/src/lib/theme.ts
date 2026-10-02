import { useCallback, useEffect, useSyncExternalStore } from 'react'

/**
 * Theme (05-frontend.md section 9.1): `'system' | 'light' | 'dark'` stored in
 * `localStorage['rushday.theme']` (every access in try/catch: private windows, blocked storage and a
 * full quota must never break a page), applied as `<html data-theme>` (removed for system, so the
 * `prefers-color-scheme` block of app.css decides) and mirrored to `<meta name="theme-color">`.
 * `public/theme-init.js` applies a stored choice before first paint; this module keeps it in sync.
 *
 * One store for the whole page, so the top-bar toggle and the account page's toggle always agree.
 */

export type ThemeMode = 'system' | 'light' | 'dark'
export type ResolvedTheme = 'light' | 'dark'

export const THEME_STORAGE_KEY = 'rushday.theme'

/** The browser chrome colour per theme: the `--background` token. */
export const THEME_COLORS: Record<ResolvedTheme, string> = { light: '#f4f5f7', dark: '#0f1216' }

const DARK_QUERY = '(prefers-color-scheme: dark)'

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
    // Storage unavailable: the choice still applies for as long as this page is open.
  }
}

export function readSystemTheme(): ResolvedTheme {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return 'light'
  return window.matchMedia(DARK_QUERY).matches ? 'dark' : 'light'
}

/** In-memory choice; undefined means "not chosen on this page yet, read storage". */
let chosenMode: ThemeMode | undefined
const listeners = new Set<() => void>()

function currentMode(): ThemeMode {
  return chosenMode ?? readStored()
}

function notify(): void {
  for (const listener of listeners) listener()
}

/** Applies a mode to `<html data-theme>` and both `<meta name="theme-color">` tags. */
export function applyTheme(mode: ThemeMode): void {
  const root = document.documentElement
  if (mode === 'system') delete root.dataset.theme
  else root.dataset.theme = mode

  for (const meta of document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')) {
    const media = meta.getAttribute('media') ?? ''
    const own: ResolvedTheme = media.includes('dark') ? 'dark' : 'light'
    meta.setAttribute('content', THEME_COLORS[mode === 'system' ? own : mode])
  }
}

export function setThemeMode(mode: ThemeMode): void {
  chosenMode = mode
  writeStored(mode)
  applyTheme(mode)
  notify()
}

function subscribeMode(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function subscribeSystem(listener: () => void): () => void {
  if (typeof window.matchMedia !== 'function') return () => {}
  const query = window.matchMedia(DARK_QUERY)
  query.addEventListener('change', listener)
  return () => query.removeEventListener('change', listener)
}

/** Test seam: forget the in-memory choice (tests clear localStorage between cases). */
export function resetThemeForTests(): void {
  chosenMode = undefined
}

export function useTheme(): {
  mode: ThemeMode
  resolved: ResolvedTheme
  setMode: (mode: ThemeMode) => void
} {
  const mode = useSyncExternalStore(subscribeMode, currentMode, (): ThemeMode => 'system')
  const system = useSyncExternalStore(
    subscribeSystem,
    readSystemTheme,
    (): ResolvedTheme => 'light',
  )
  const resolved = mode === 'system' ? system : mode

  // Keep the document in step with the stored choice (theme-init.js did this once before paint).
  useEffect(() => {
    applyTheme(mode)
  }, [mode])

  const setMode = useCallback((next: ThemeMode) => setThemeMode(next), [])
  return { mode, resolved, setMode }
}
