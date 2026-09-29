import { AlertTriangle, CheckCircle2, OctagonAlert } from 'lucide-react'

import type { OpsSnapshot } from '@/api/types/ops'
import { formatDecimal, formatNumber } from '@/lib/format'
import { cn } from '@/lib/cn'

export type HealthState = 'ok' | 'busy' | 'struggling'

/**
 * The three health states (04-performance-and-ops.md section 6.3, 05-frontend.md section 10):
 * "struggling" (database waits or server errors in the last minute) beats "busy" (some visitors
 * asked to try again), which beats "ok".
 */
// Shares a plain function with HealthSummary.test.tsx; a full reload on edit is an acceptable trade.
// eslint-disable-next-line react-refresh/only-export-components
export function healthState(snapshot: OpsSnapshot, previous: OpsSnapshot | undefined): HealthState {
  const { last60s } = snapshot.http
  const waitTimeoutsGrew =
    previous !== undefined && snapshot.db.waitTimeoutsTotal > previous.db.waitTimeoutsTotal
  if (last60s.status5xx > 0 || waitTimeoutsGrew) return 'struggling'
  if (last60s.shed503 > 0 || last60s.rateLimited429 > 0) return 'busy'
  return 'ok'
}

const COPY: Record<
  HealthState,
  { heading: string; sentence: string; icon: typeof CheckCircle2; tone: string }
> = {
  ok: {
    heading: 'Running normally',
    sentence: 'No errors and nobody has been asked to try again in the last minute.',
    icon: CheckCircle2,
    tone: 'text-success',
  },
  busy: {
    heading: 'Busy: some visitors are being asked to try again',
    sentence: 'The portal is shedding excess load on purpose rather than letting everyone wait.',
    icon: AlertTriangle,
    tone: 'text-warning',
  },
  struggling: {
    heading: 'Struggling: database waits or server errors in the last minute',
    sentence:
      'Something is going wrong beyond planned load shedding. Check the technical details below.',
    icon: OctagonAlert,
    tone: 'text-danger',
  },
}

export interface HealthSummaryProps {
  snapshot: OpsSnapshot
  previous: OpsSnapshot | undefined
  /** Greyed and captioned when the most recent poll failed (05-frontend.md section 10). */
  stale?: boolean
}

/** The plain-language health line at the top of `/admin/ops` (`role="status"`). */
export function HealthSummary({ snapshot, previous, stale = false }: HealthSummaryProps) {
  const state = healthState(snapshot, previous)
  const { heading, sentence, icon: Icon, tone } = COPY[state]
  const { last60s } = snapshot.http
  const successPct =
    last60s.requests > 0
      ? formatDecimal(((last60s.requests - last60s.status5xx) / last60s.requests) * 100, 1)
      : '100.0'
  const askedToTryAgain = last60s.shed503 + last60s.rateLimited429

  return (
    <div
      role="status"
      className={cn(
        'flex flex-col gap-2 rounded-lg border border-border bg-surface p-4 shadow-card transition-opacity',
        stale && 'opacity-60',
      )}
    >
      <div className="flex items-center gap-2">
        <Icon aria-hidden="true" className={cn('size-5 shrink-0', tone)} />
        <p className={cn('text-lg font-semibold', tone)}>{heading}</p>
      </div>
      <p className="text-sm text-muted">{sentence}</p>
      <p className="text-sm text-muted">
        Last minute: {formatNumber(last60s.requests)} requests, {successPct}% succeeded, typical
        response {formatNumber(Math.round(last60s.p50Ms))} ms, slowest 5% under{' '}
        {formatNumber(Math.round(last60s.p95Ms))} ms, {formatNumber(askedToTryAgain)} asked to try
        again, database{' '}
        {snapshot.db.pendingRequests > 0 ||
        snapshot.db.waitTimeoutsTotal > (previous?.db.waitTimeoutsTotal ?? 0)
          ? 'degraded'
          : 'ok'}
        .
      </p>
    </div>
  )
}
