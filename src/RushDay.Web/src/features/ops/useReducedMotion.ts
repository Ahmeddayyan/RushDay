import { useSyncExternalStore } from 'react'

const QUERY = '(prefers-reduced-motion: reduce)'

function subscribe(listener: () => void): () => void {
  if (typeof window.matchMedia !== 'function') return () => {}
  const query = window.matchMedia(QUERY)
  query.addEventListener('change', listener)
  return () => query.removeEventListener('change', listener)
}

function snapshot(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia(QUERY).matches
}

/**
 * `prefers-reduced-motion` (05-frontend.md sections 11 and 12): every Recharts series sets
 * `isAnimationActive={false}` when this is true.
 */
export function useReducedMotion(): boolean {
  return useSyncExternalStore(subscribe, snapshot, () => false)
}
