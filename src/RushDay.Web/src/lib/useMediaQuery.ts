import { useCallback, useSyncExternalStore } from 'react'

/**
 * Whether a CSS media query matches, kept in sync as it changes. For layouts that must move an
 * element rather than restyle it (the marks grid's action bar is pinned to the bottom of a phone
 * screen), where rendering it twice and hiding one copy with CSS would duplicate controls.
 * False where `matchMedia` is missing (server rendering, old test environments).
 */
export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback(
    (listener: () => void) => {
      if (typeof window.matchMedia !== 'function') return () => {}
      const list = window.matchMedia(query)
      list.addEventListener('change', listener)
      return () => list.removeEventListener('change', listener)
    },
    [query],
  )
  return useSyncExternalStore(
    subscribe,
    () => typeof window.matchMedia === 'function' && window.matchMedia(query).matches,
    () => false,
  )
}
