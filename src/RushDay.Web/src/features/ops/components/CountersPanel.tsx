import type { OpsSnapshot } from '@/api/types/ops'
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui'
import { formatDecimal, formatNumber } from '@/lib/format'

import { formatUptime } from '../formatUptime'

export interface CountersPanelProps {
  snapshot: OpsSnapshot
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4 py-1.5 text-sm">
      <span className="text-muted">{label}</span>
      <span className="font-medium tabular-nums text-text">{value}</span>
    </div>
  )
}

/** Totals since the server started (05-frontend.md section 10): enrolment outcomes, cache hit rates,
 * database round trips per dashboard, and sign-ins. */
export function CountersPanel({ snapshot }: CountersPanelProps) {
  const { enrolment, cache, dashboard, auth, uptimeSeconds } = snapshot
  const uptime = formatUptime(uptimeSeconds)

  return (
    <Card>
      <CardHeader>
        <CardTitle>Counters</CardTitle>
        <CardDescription>Totals since the server started {uptime} ago.</CardDescription>
      </CardHeader>
      <div className="grid gap-6 sm:grid-cols-2">
        <div>
          <h3 className="mb-1 text-sm font-semibold text-text">Enrolment requests</h3>
          <div className="divide-y divide-border">
            <Row label="Accepted" value={formatNumber(enrolment.accepted)} />
            <Row label="Full" value={formatNumber(enrolment.rejected.moduleFull)} />
            <Row
              label="Already enrolled"
              value={formatNumber(enrolment.rejected.alreadyEnrolled)}
            />
            <Row label="Closed" value={formatNumber(enrolment.rejected.windowClosed)} />
            <Row label="Over credit limit" value={formatNumber(enrolment.rejected.creditLimit)} />
            <Row label="Completed already" value={formatNumber(enrolment.rejected.resultsExist)} />
            <Row label="Other" value={formatNumber(enrolment.rejected.other)} />
          </div>
        </div>
        <div>
          <h3 className="mb-1 text-sm font-semibold text-text">Answered from memory</h3>
          <div className="divide-y divide-border">
            {cache.map((entry) => {
              const total = entry.hits + entry.misses
              const pct = total > 0 ? formatDecimal((entry.hits / total) * 100, 0) : '0'
              return <Row key={entry.name} label={entry.name} value={`${pct}%`} />
            })}
            <Row
              label="Database round trips per dashboard"
              value={`${formatDecimal(dashboard.queriesPerRequest, 0)} (was 15)`}
            />
          </div>
        </div>
        <div>
          <h3 className="mb-1 text-sm font-semibold text-text">Sign-ins</h3>
          <div className="divide-y divide-border">
            <Row label="Succeeded" value={formatNumber(auth.loginsSucceeded)} />
            <Row label="Failed" value={formatNumber(auth.loginsFailed)} />
            <Row label="Lockouts" value={formatNumber(auth.lockouts)} />
          </div>
        </div>
      </div>
    </Card>
  )
}
