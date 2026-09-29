import type { OpsDb } from '@/api/types/ops'
import { Meter } from '@/components/ui'
import { formatNumber } from '@/lib/format'

export interface PoolMeterProps {
  db: OpsDb
}

/**
 * The database pool meter (05-frontend.md sections 10 and 11): warning at 80% busy, danger at 95%,
 * always paired with the numbers as text. `waitTimeoutsTotal` is "what broke the first version"
 * (04-performance-and-ops.md section 6.3).
 */
export function PoolMeter({ db }: PoolMeterProps) {
  return (
    <div className="space-y-3 rounded-lg border border-border bg-surface p-4 shadow-card">
      <Meter
        label="Database connections in use"
        value={db.poolBusy}
        max={db.poolMax}
        valueText={`${formatNumber(db.poolBusy)} of ${formatNumber(db.poolMax)} connections in use`}
        showLabel
        warningAt={0.8}
        dangerAt={0.95}
        statusText={
          db.poolMax > 0 && db.poolBusy / db.poolMax >= 0.95
            ? 'Near the limit'
            : db.poolMax > 0 && db.poolBusy / db.poolMax >= 0.8
              ? 'Getting busy'
              : undefined
        }
      />
      <p className="text-sm text-muted">
        Pending requests waiting for a connection: {formatNumber(db.pendingRequests)}.
      </p>
      <p className="text-sm text-muted">
        Times a page gave up waiting for the database since the server started:{' '}
        {formatNumber(db.waitTimeoutsTotal)}.
      </p>
    </div>
  )
}
