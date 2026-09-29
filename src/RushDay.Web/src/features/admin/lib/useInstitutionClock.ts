import { useCallback } from 'react'

import { useServerClock } from '@/api/endpoints/public'

import { currentTime } from './zonedTime'

/**
 * The institution's time zone (from `['public','status']`, D26) and the server's clock, so
 * "now" in a picker's minimum is the server's now even on a laptop whose clock is out.
 */
export function useInstitutionClock(): {
  timeZone: string
  academicYear: string | undefined
  /** Milliseconds on the server clock; call it in handlers and effects, not while rendering. */
  now: () => number
} {
  const { offsetMs, timeZone, status } = useServerClock()
  const now = useCallback(() => currentTime() + offsetMs, [offsetMs])
  return { timeZone, academicYear: status?.academicYear, now }
}
