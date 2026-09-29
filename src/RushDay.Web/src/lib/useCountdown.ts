import { useEffect, useRef, useState } from 'react'

/**
 * Time left until an instant, measured against the server's clock (05-frontend.md section 10,
 * `/student` "Results release"). `serverOffsetMs` is `Date.parse(status.serverTime) - Date.now()`
 * captured when `['public','status']` was fetched, so a laptop whose clock is five minutes out
 * still counts down to the server's 09:00.
 *
 * When the instant passes while the component is mounted, `onElapsed` runs exactly once, after a
 * random delay of 0 to `spreadMs` (30 s by default). The delay is the point: on results day 20,000
 * dashboards are open on the same countdown, and if every one refetched in the same second the
 * portal would take a 20,000-request spike at 09:00:00. Spreading them uniformly over 30 seconds
 * turns that into roughly 670 requests per second, which the server absorbs.
 */

export const RELEASE_SPREAD_MS = 30_000

export interface CountdownParts {
  days: number
  hours: number
  minutes: number
  seconds: number
}

export interface CountdownState extends CountdownParts {
  /** Milliseconds left on the server clock; 0 once the instant has passed. */
  remainingMs: number
  elapsed: boolean
}

export interface UseCountdownOptions {
  serverOffsetMs?: number
  onElapsed?: () => void
  spreadMs?: number
}

export function splitDuration(ms: number): CountdownParts {
  const total = Math.max(0, Math.floor(ms / 1000))
  return {
    days: Math.floor(total / 86_400),
    hours: Math.floor((total % 86_400) / 3600),
    minutes: Math.floor((total % 3600) / 60),
    seconds: total % 60,
  }
}

/**
 * "About 11 minutes to go": derived from whole minutes, so a live region fed with it changes at
 * most once a minute (05-frontend.md section 9.3).
 */
export function aboutSentence(remainingMs: number, elapsedText = 'Due now'): string {
  if (remainingMs <= 0) return elapsedText
  const minutes = Math.ceil(remainingMs / 60_000)
  if (minutes <= 1) return 'Less than a minute to go'
  if (minutes < 120) return `About ${minutes} minutes to go`
  const hours = Math.round(minutes / 60)
  if (hours < 48) return `About ${hours} hours to go`
  return `About ${Math.round(hours / 24)} days to go`
}

function toMs(target: string | number | null | undefined): number | null {
  if (target === null || target === undefined) return null
  const ms = typeof target === 'number' ? target : Date.parse(target)
  return Number.isNaN(ms) ? null : ms
}

export function useCountdown(
  target: string | number | null | undefined,
  { serverOffsetMs = 0, onElapsed, spreadMs = RELEASE_SPREAD_MS }: UseCountdownOptions = {},
): CountdownState | null {
  const targetMs = toMs(target)
  const [now, setNow] = useState(() => Date.now())
  const onElapsedRef = useRef(onElapsed)
  const pendingSeenRef = useRef(false)
  const firedRef = useRef(false)

  useEffect(() => {
    onElapsedRef.current = onElapsed
  }, [onElapsed])

  // Tick once a second until the instant has passed on the server clock.
  useEffect(() => {
    if (targetMs === null) return
    const tick = () => {
      const current = Date.now()
      setNow(current)
      if (current + serverOffsetMs >= targetMs) clearInterval(timer)
    }
    const timer = setInterval(tick, 1000)
    return () => clearInterval(timer)
  }, [targetMs, serverOffsetMs])

  const remainingMs = targetMs === null ? null : Math.max(0, targetMs - (now + serverOffsetMs))
  const elapsed = remainingMs !== null && remainingMs <= 0

  // A new target starts a new countdown.
  useEffect(() => {
    pendingSeenRef.current = false
    firedRef.current = false
  }, [targetMs])

  useEffect(() => {
    if (targetMs === null) return
    if (!elapsed) {
      pendingSeenRef.current = true
      return
    }
    // Only an instant that passes while mounted fires; one already past at mount does not.
    if (!pendingSeenRef.current || firedRef.current) return
    firedRef.current = true
    const timer = setTimeout(() => onElapsedRef.current?.(), Math.random() * spreadMs)
    return () => clearTimeout(timer)
  }, [targetMs, elapsed, spreadMs])

  if (remainingMs === null) return null
  return { remainingMs, elapsed, ...splitDuration(remainingMs) }
}
