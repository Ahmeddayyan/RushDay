import { useEffect, useState } from 'react'

/**
 * Returns `value` once it has stopped changing for `delayMs` (the catalogue search uses 250 ms,
 * 05-frontend.md section 10), so typing does not send one request per keystroke.
 */
export function useDebouncedValue<T>(value: T, delayMs = 250): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return debounced
}
