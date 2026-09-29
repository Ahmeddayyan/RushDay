import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'

/**
 * List filters live in the URL (05-frontend.md section 7: "admin pages; list filters in the URL"),
 * so a filtered view can be bookmarked, shared with a colleague and restored with Back. Changing a
 * filter drops `page` (back to the first page); `replace` keeps history to one entry per page.
 */
export type UrlPatch<K extends string> = Partial<Record<K | 'page', string | number | null>>

export function useUrlFilters<K extends string>(keys: readonly K[]) {
  const [params, setParams] = useSearchParams()

  const values = {} as Record<K, string>
  for (const key of keys) values[key] = params.get(key) ?? ''

  const page = Math.max(1, Math.floor(Number(params.get('page'))) || 1)

  const update = useCallback(
    (patch: UrlPatch<K>) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous)
          for (const [key, value] of Object.entries(patch) as [string, string | number | null][]) {
            if (value === null || value === '' || (key === 'page' && value === 1)) next.delete(key)
            else next.set(key, String(value))
          }
          if (!('page' in patch)) next.delete('page')
          return next
        },
        { replace: true },
      )
    },
    [setParams],
  )

  return { values, page, update }
}

/**
 * A one-shot `?{name}=1` flag, such as the overview's quick actions ("Provision account" opens
 * `/admin/accounts?provision=1`): true on the first render when present, then removed from the URL
 * so a reload or Back does not open the dialog again.
 */
export function useOneShotFlag(name: string): boolean {
  const [params, setParams] = useSearchParams()
  const [initial] = useState(() => params.get(name) === '1')

  useEffect(() => {
    if (params.get(name) !== '1') return
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous)
        next.delete(name)
        return next
      },
      { replace: true },
    )
  }, [name, params, setParams])

  return initial
}
