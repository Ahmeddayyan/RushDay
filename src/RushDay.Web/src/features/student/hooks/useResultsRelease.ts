import { useCallback, useEffect, useRef, useState } from 'react'
import { useQueryClient, type QueryKey } from '@tanstack/react-query'

import { useServerClock } from '@/api/endpoints/public'
import { queryKeys } from '@/api/keys'
import { toast } from '@/lib/toast'
import { RELEASE_SPREAD_MS, useCountdown } from '@/lib/useCountdown'

/** The three caches a results release refreshes: the dashboard, the results page and public status. */
const RELEASE_KEYS: readonly QueryKey[] = [
  queryKeys.student.dashboard,
  queryKeys.student.results,
  queryKeys.publicStatus,
]

function isReleaseKey(key: QueryKey): boolean {
  return RELEASE_KEYS.some(
    (releaseKey) =>
      releaseKey.length === key.length && releaseKey.every((part, index) => part === key[index]),
  )
}

export type ReleasePhase =
  /** Nothing scheduled (or nothing left to count down to). */
  | 'idle'
  /** Counting down to `publishAt` on the server's clock. */
  | 'counting'
  /** The instant has passed: waiting the random spread, then refetching. */
  | 'releasing'
  /** The refetch came back: "Your results are in". */
  | 'released'
  /** The refetch failed (the portal is busy): offer to try again. */
  | 'failed'

export interface ResultsRelease {
  phase: ReleasePhase
  /** Server clock minus browser clock, for <Countdown>. */
  offsetMs: number
  timeZone: string
  /** After `failed`: refetch again now. */
  retry: () => void
}

export interface UseResultsReleaseOptions {
  /** Runs once the refetched results are in (the card moves focus to its heading). */
  onReleased?: () => void
}

/**
 * Results release at the instant (05-frontend.md section 10, `/student`). `useCountdown` counts down
 * to `publishAt` against the server clock. When the instant passes while the page is open, it waits
 * a random 0–30 s before doing anything: on results day 20,000 dashboards are open on the same
 * countdown, and if every one refetched in the same second the portal would take 20,000 requests at
 * 09:00:00; spread uniformly over 30 s that is about 670 requests per second, which it absorbs.
 * Meanwhile the card shows "Results are being released…". Then it invalidates the dashboard, the
 * results and public status exactly once and, when they are back, reports `released` (the card says
 * "Your results are in", a toast says it too and focus moves to the card's heading).
 *
 * Data that was already past its instant when it arrived (a dashboard cached from before 09:00) is
 * released the same way, with the same spread.
 */
export function useResultsRelease(
  publishAt: string | null | undefined,
  { onReleased }: UseResultsReleaseOptions = {},
): ResultsRelease {
  const queryClient = useQueryClient()
  const { offsetMs, timeZone } = useServerClock()
  const target = publishAt ?? null

  const [inFlight, setInFlight] = useState<string | null>(null)
  const [released, setReleased] = useState<string | null>(null)
  const [failed, setFailed] = useState<string | null>(null)
  const startedRef = useRef<string | null>(null)
  const seenPendingRef = useRef<string | null>(null)
  const onReleasedRef = useRef(onReleased)

  useEffect(() => {
    onReleasedRef.current = onReleased
  }, [onReleased])

  const release = useCallback(
    (instant: string, force = false) => {
      if (startedRef.current === instant && !force) return
      startedRef.current = instant
      setFailed(null)
      setInFlight(instant)
      void queryClient
        .invalidateQueries({ predicate: (query) => isReleaseKey(query.queryKey) })
        .then(() => {
          // Only what is on screen counts: the dashboard or the results page, whichever is open.
          const refetchFailed = queryClient
            .getQueryCache()
            .findAll({
              predicate: (query) => isReleaseKey(query.queryKey) && query.queryKey[0] === 'student',
            })
            .some((query) => query.getObserversCount() > 0 && query.state.status === 'error')
          setInFlight(null)
          if (refetchFailed) {
            setFailed(instant)
            return
          }
          setReleased(instant)
          toast.success('Your results are in.', { id: 'results-released' })
          onReleasedRef.current?.()
        })
    },
    [queryClient],
  )

  const onElapsed = useCallback(() => {
    if (target) release(target)
  }, [target, release])

  const countdown = useCountdown(target, { serverOffsetMs: offsetMs, onElapsed })
  const elapsed = countdown?.elapsed ?? false

  // Remember that this instant was seen still in the future: useCountdown then fires onElapsed.
  useEffect(() => {
    if (target && countdown && !countdown.elapsed) seenPendingRef.current = target
  })

  // Already past when it arrived: release after the same random spread.
  useEffect(() => {
    if (!target || !elapsed || seenPendingRef.current === target) return
    if (startedRef.current === target) return
    const timer = setTimeout(() => release(target), Math.random() * RELEASE_SPREAD_MS)
    return () => clearTimeout(timer)
  }, [target, elapsed, release])

  let phase: ReleasePhase = 'idle'
  if (inFlight) phase = 'releasing'
  else if (target && failed === target) phase = 'failed'
  else if (target && countdown && !countdown.elapsed) phase = 'counting'
  else if (target && elapsed && released !== target) phase = 'releasing'
  else if (released) phase = 'released'

  return {
    phase,
    offsetMs,
    timeZone,
    retry: () => {
      if (target) release(target, true)
    },
  }
}
